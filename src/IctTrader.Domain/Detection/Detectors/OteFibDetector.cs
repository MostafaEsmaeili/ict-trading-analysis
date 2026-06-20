using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects an Optimal Trade Entry (plan §2.5.1 step 7) and emits <see cref="ConfluenceCondition.OteZone"/> (0.7,
/// not required). It projects the configured 62–79% band (sweet spot 70.5%) onto the pre-validated displacement
/// leg — it does NOT re-quantify displacement — and requires a same-direction, same-timeframe FVG or order-block
/// key level to fall inside the band, choosing the level nearest the sweet spot. A fully retraced leg
/// (OteVoidedOnFullRetrace) or no overlapping array (OteSkippedNoOverlap) yields no match.
/// </summary>
public sealed class OteFibDetector : ISetupDetector
{
    private readonly OteOptions _options;

    public OteFibDetector(OteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.OteZone;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.LastDisplacement is not { } leg || leg.Retraced)
        {
            return DetectorResult.NoMatch; // no leg, or it fully retraced (OteVoidedOnFullRetrace)
        }

        var lower = Retrace(leg, _options.LowerFib);
        var upper = Retrace(leg, _options.EffectiveUpperFib);
        var sweetSpot = Retrace(leg, _options.SweetSpotFib);
        var band = new OteZone(
            new Price(Math.Min(lower, upper)), new Price(Math.Max(lower, upper)), new Price(sweetSpot));

        var keyLevel = NearestArrayLevelInBand(context, leg.Direction, leg.Timeframe, band, sweetSpot);
        if (keyLevel is not { } level)
        {
            return DetectorResult.NoMatch; // OteSkippedNoOverlap
        }

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = leg.Direction.ToString(),
            [EvidenceKeys.OteSweetSpot] = sweetSpot,
            [EvidenceKeys.Timeframe] = leg.Timeframe.ToString(),
        };

        return DetectorResult.Match(
            leg.Direction, level, ReasonFragments.OteEntry(leg.Direction, sweetSpot, leg.Timeframe), evidence);
    }

    // Retrace the leg from its terminus back toward its origin by fraction f (works for both directions).
    private static decimal Retrace(Displacement leg, decimal fraction)
        => leg.Terminus.Value + (fraction * (leg.Origin.Value - leg.Terminus.Value));

    private static decimal? NearestArrayLevelInBand(
        MarketContext context, Direction direction, Timeframe timeframe, OteZone band, decimal sweetSpot)
    {
        decimal? best = null;
        var bestDistance = decimal.MaxValue;

        void Consider(decimal level)
        {
            if (!band.Contains(new Price(level)))
            {
                return;
            }

            var distance = Math.Abs(level - sweetSpot);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = level;
            }
        }

        foreach (var fvg in context.OpenFvgs)
        {
            if (fvg.IsOpen && fvg.Direction == direction && fvg.Timeframe == timeframe)
            {
                Consider(fvg.Midpoint);
            }
        }

        foreach (var orderBlock in context.OpenOrderBlocks)
        {
            if (orderBlock.IsOpen && orderBlock.Direction == direction && orderBlock.Timeframe == timeframe)
            {
                Consider(orderBlock.Open.Value);
            }
        }

        return best;
    }
}
