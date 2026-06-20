using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection;

/// <summary>
/// The SINGLE source of truth for the §2.5 OTE entry level (plan §2.5.1 step 7). It projects the configured
/// 62–79% band (sweet spot 70.5%) onto the pre-validated displacement leg — it does NOT re-quantify
/// displacement — and selects the same-direction, same-timeframe FVG/OB key level nearest the sweet spot.
/// <see cref="Detectors.OteFibDetector"/> (which emits <see cref="ConfluenceCondition.OteZone"/>) and
/// <see cref="Detectors.DrawOnLiquidityDetector"/> (which anchors its reward-to-risk frame at the entry) both
/// consume this, so the entry level cannot drift between the two detectors.
/// </summary>
public static class OteEntryResolver
{
    /// <summary>The resolved OTE entry on the current displacement leg, or null when none qualifies.</summary>
    public readonly record struct OteEntry(
        decimal Level, OteZone Band, Direction Direction, Timeframe Timeframe, decimal SweetSpot);

    public static OteEntry? Resolve(MarketContext context, OteOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (context.LastDisplacement is not { } leg || leg.Retraced)
        {
            return null; // no leg, or it fully retraced (OteVoidedOnFullRetrace)
        }

        var lower = Retrace(leg, options.LowerFib);
        var upper = Retrace(leg, options.EffectiveUpperFib);
        var sweetSpot = Retrace(leg, options.SweetSpotFib);
        var band = new OteZone(
            new Price(Math.Min(lower, upper)), new Price(Math.Max(lower, upper)), new Price(sweetSpot));

        var level = NearestArrayLevelInBand(context, leg.Direction, leg.Timeframe, band, sweetSpot);
        return level is { } entry
            ? new OteEntry(entry, band, leg.Direction, leg.Timeframe, sweetSpot)
            : null; // OteSkippedNoOverlap
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
