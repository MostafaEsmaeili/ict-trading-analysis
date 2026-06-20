using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects an energetic displacement candle (plan §2.5.1 step 5, §2.5.7 caveat 5) and publishes the leg
/// whose 50% the FVG/OTE premium-discount gate reads. A NON-scoring precondition — it carries no
/// <see cref="ConfluenceCondition"/>; the single <c>DisplacementMss</c> weight is owned by the MSS detector.
/// Energy is quantified as body-to-range ≥ floor AND body ≥ ATR×multiple (an absolute pip floor is OFF by
/// default so it is never a silent extra filter). Invalidation: the prior leg is marked retraced when price
/// closes back beyond its origin.
/// </summary>
public sealed class DisplacementDetector : ISetupDetector
{
    private readonly DisplacementOptions _options;

    public DisplacementDetector(DisplacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => null;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        InvalidateRetracedLeg(context, current);

        var window = context.Window(current.Timeframe);
        if (window.Count < _options.AtrPeriod + 1)
        {
            return DetectorResult.NoMatch; // ATR warmup
        }

        var range = current.High - current.Low;
        if (range <= 0m)
        {
            return DetectorResult.NoMatch;
        }

        var body = Math.Abs(current.Close - current.Open);
        var atr = AverageTrueRange(window, _options.AtrPeriod);

        var energetic = body / range >= _options.MinBodyToRangeRatio && body >= _options.AtrMultiple * atr;
        if (_options.MinDisplacementPips > 0m)
        {
            energetic &= context.SymbolSpec.PriceToPips(body).Value >= _options.MinDisplacementPips;
        }

        if (!energetic || current.Close == current.Open)
        {
            return DetectorResult.NoMatch; // weak / wick-only expansion
        }

        var direction = current.Close > current.Open ? Direction.Bullish : Direction.Bearish;
        var (origin, terminus) = direction == Direction.Bullish
            ? (current.Low, current.High)
            : (current.High, current.Low);

        var leg = new Displacement(direction, current.Timeframe, new Price(origin), new Price(terminus), current.OpenTimeUtc);
        context.SetDisplacement(leg);

        var pips = context.SymbolSpec.PriceToPips(body).Value;
        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = direction.ToString(),
            [EvidenceKeys.DisplacementPips] = pips,
            [EvidenceKeys.Timeframe] = current.Timeframe.ToString(),
        };

        return DetectorResult.Match(
            direction, terminus, ReasonFragments.Displacement(direction, pips, current.Timeframe), evidence);
    }

    private static void InvalidateRetracedLeg(MarketContext context, Candle current)
    {
        var leg = context.LastDisplacement;
        if (leg is null || leg.Retraced)
        {
            return;
        }

        var retraced = leg.Direction == Direction.Bullish
            ? current.Close < leg.Origin.Value
            : current.Close > leg.Origin.Value;

        if (retraced)
        {
            leg.MarkRetraced();
        }
    }

    private static decimal AverageTrueRange(IReadOnlyList<Candle> window, int period)
    {
        var sum = 0m;
        for (var i = window.Count - period; i < window.Count; i++)
        {
            var candle = window[i];
            var previousClose = window[i - 1].Close;
            var trueRange = Math.Max(
                candle.High - candle.Low,
                Math.Max(Math.Abs(candle.High - previousClose), Math.Abs(candle.Low - previousClose)));
            sum += trueRange;
        }

        return sum / period;
    }
}
