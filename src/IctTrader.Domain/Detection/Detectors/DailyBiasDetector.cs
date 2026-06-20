using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects the daily bias (plan §2.5.1 step 1, §2.5.10) and emits <see cref="ConfluenceCondition.BiasAligned"/>
/// (0.85, required). Bias is the dealing-range premium/discount read of the current price via the shared
/// <see cref="EquilibriumBoundaryPolicy"/>: discount ⇒ bullish, premium ⇒ bearish, exactly-at-equilibrium (or a
/// degenerate range) ⇒ NEUTRAL (no trade). It always sets <see cref="MarketContext.Bias"/>; it emits the match
/// only for a one-sided bias (and, when enabled, after the §2.5.10 consecutive-close corroboration).
/// </summary>
public sealed class DailyBiasDetector : ISetupDetector
{
    private readonly DailyBiasOptions _options;

    public DailyBiasDetector(DailyBiasOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.BiasAligned;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.DailyRange is not { } range)
        {
            context.SetBias(null);
            return DetectorResult.NoMatch; // no dealing range yet -> neutral
        }

        var positionPercent = range.PositionPercent(new Price(current.Close));
        var half = EquilibriumBoundaryPolicy.Classify(positionPercent, _options.EquilibriumPercent * 100m);
        var bias = EquilibriumBoundaryPolicy.BiasFor(half);
        context.SetBias(bias);

        if (bias is not { } direction)
        {
            return DetectorResult.NoMatch; // equilibrium / degenerate range -> NEUTRAL (no trade)
        }

        if (_options.RequireConsecutiveCloseConfirmation && !IsConfirmedByConsecutiveCloses(context, current, direction))
        {
            return DetectorResult.NoMatch; // bias set, but not corroborated by N directional closes
        }

        var equilibrium = range.Equilibrium(_options.EquilibriumPercent);
        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = direction.ToString(),
            [EvidenceKeys.EquilibriumPrice] = equilibrium,
            [EvidenceKeys.PositionPercent] = positionPercent,
        };

        return DetectorResult.Match(
            direction, equilibrium, ReasonFragments.DailyBias(direction, positionPercent), evidence);
    }

    private bool IsConfirmedByConsecutiveCloses(MarketContext context, Candle current, Direction direction)
    {
        var window = context.Window(current.Timeframe);
        if (window.Count < _options.ConsecutiveCloseCount)
        {
            return false;
        }

        for (var i = window.Count - _options.ConsecutiveCloseCount; i < window.Count; i++)
        {
            var aligned = direction == Direction.Bullish ? window[i].IsUpClose : window[i].IsDownClose;
            if (!aligned)
            {
                return false;
            }
        }

        return true;
    }
}
