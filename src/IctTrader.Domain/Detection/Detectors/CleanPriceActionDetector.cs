using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Emits the OPTIONAL <see cref="ConfluenceCondition.CleanPriceAction"/> confluence (0.40, plan §2.5.6/§2.5.8). It
/// reads the validated displacement leg from <see cref="MarketContext.LastDisplacement"/> (its OriginAtUtc..AtUtc
/// member candles in the leg's timeframe window) and measures how "clean" the run is — the §2.5.6 low-resistance vs
/// high-resistance (HRLR) distinction — as the net body-to-range ratio Σ|body| / Σrange. A leg whose ratio meets
/// <see cref="CleanPriceActionOptions.CleanBodyRatio"/> is a clean displacement (decisive bodies, little chop) and
/// contributes confluence; a wicky / overlapping leg does not.
///
/// <para>It emits the bias direction (so the FSM only counts it when it agrees with the MSS lock); it is a confluence
/// (scoring-only), NOT a RequiredCondition — its absence never blocks a setup. Pure: it reads only the leg candles.</para>
/// </summary>
public sealed class CleanPriceActionDetector : ISetupDetector
{
    private readonly CleanPriceActionOptions _options;

    public CleanPriceActionDetector(CleanPriceActionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.CleanPriceAction;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_options.Enabled || context.Bias is not { } bias || context.LastDisplacement is not { } leg)
        {
            return DetectorResult.NoMatch; // disabled, neutral bias, or no displacement leg
        }

        if (BodyToRangeRatio(context, leg.Timeframe, leg.OriginAtUtc, leg.AtUtc) is not { } ratio)
        {
            return DetectorResult.NoMatch; // the leg candles are not in the window (degenerate / pruned)
        }

        if (ratio < _options.CleanBodyRatio)
        {
            return DetectorResult.NoMatch; // a high-resistance (wicky / overlapping) run, not a clean displacement
        }

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = bias.ToString(),
            [EvidenceKeys.CleanBodyRatio] = ratio,
        };

        return DetectorResult.Match(
            bias, keyLevel: null, ReasonFragments.CleanPriceAction(bias, ratio), evidence);
    }

    // Σ|body| / Σrange over the leg's member candles (OriginAtUtc..AtUtc inclusive). Null when no member candle is in
    // the window or the total range is degenerate (zero), so the ratio is never divided by zero.
    private static decimal? BodyToRangeRatio(
        MarketContext context, Timeframe timeframe, DateTimeOffset originAtUtc, DateTimeOffset terminusAtUtc)
    {
        decimal totalBody = 0m;
        decimal totalRange = 0m;
        var member = false;

        foreach (var candle in context.Window(timeframe))
        {
            if (candle.OpenTimeUtc < originAtUtc || candle.OpenTimeUtc > terminusAtUtc)
            {
                continue;
            }

            member = true;
            totalBody += Math.Abs(candle.Close - candle.Open);
            totalRange += candle.High - candle.Low;
        }

        if (!member || totalRange <= 0m)
        {
            return null;
        }

        return totalBody / totalRange;
    }
}
