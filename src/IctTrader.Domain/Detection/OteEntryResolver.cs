using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection;

/// <summary>
/// The SINGLE source of truth for the §2.5 OTE entry level (plan §2.5.1 step 7). It projects the configured
/// 62–79% band (sweet spot 70.5%) onto the pre-validated displacement leg — it does NOT re-quantify
/// displacement — and selects the same-direction, same-timeframe FVG/OB key level inside the band.
/// <see cref="Detectors.OteFibDetector"/> (which emits <see cref="ConfluenceCondition.OteZone"/>) and
/// <see cref="Detectors.DrawOnLiquidityDetector"/> (which anchors its reward-to-risk frame at the entry) both
/// consume this, so the entry level cannot drift between the two detectors.
///
/// <para>Selection is governed by <see cref="OteSelectionPolicy"/>: the default nearest-the-sweet-spot pick, or
/// (FVG-SEM-2a) the strict-first-FVG SHALLOWEST in-band gap. The resolver stays PURE — it SELECTS only; the
/// <c>IsSelectedEntry</c>/<c>Stacked</c> VO marking is done by the owning <see cref="Detectors.OteFibDetector"/>.</para>
/// </summary>
public static class OteEntryResolver
{
    /// <summary>
    /// How the in-band entry level is chosen (FVG-SEM-2a). <paramref name="StrictFirstFvg"/> switches from the
    /// default nearest-sweet-spot pick to Ep3's "first higher fair value gap" (the SHALLOWEST in-band gap);
    /// <paramref name="StackProximityPips"/> is the stacked-detection window between the selected gap and the
    /// next-deeper one.
    /// </summary>
    public readonly record struct OteSelectionPolicy(bool StrictFirstFvg, decimal StackProximityPips);

    /// <summary>The resolved OTE entry on the current displacement leg, or null when none qualifies.</summary>
    public readonly record struct OteEntry(
        decimal Level,
        OteZone Band,
        Direction Direction,
        Timeframe Timeframe,
        decimal SweetSpot,
        FairValueGap? SelectedFvg,
        decimal? StackedFartherBound);

    public static OteEntry? Resolve(MarketContext context, OteOptions options, OteSelectionPolicy policy)
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

        var eligibleFvgs = EligibleFvgs(context, leg.Direction, leg.Timeframe, band);
        var selection = SelectLevel(context, leg, band, sweetSpot, eligibleFvgs, policy);
        if (selection is not { } chosen)
        {
            return null; // OteSkippedNoOverlap
        }

        var stackedFartherBound = chosen.Fvg is { } selectedFvg
            ? StackedFartherBound(context, leg.Direction, selectedFvg, policy.StackProximityPips)
            : null;

        return new OteEntry(
            chosen.Level, band, leg.Direction, leg.Timeframe, sweetSpot, chosen.Fvg, stackedFartherBound);
    }

    // Retrace the leg from its terminus back toward its origin by fraction f (works for both directions).
    private static decimal Retrace(Displacement leg, decimal fraction)
        => leg.Terminus.Value + (fraction * (leg.Origin.Value - leg.Terminus.Value));

    // Depth of a candidate level on the leg: 0 at the terminus (shallowest, reached first on the retrace) -> 1 at
    // the origin (deepest). Inverse of Retrace, sign-correct for both directions.
    private static decimal Depth(Displacement leg, decimal level)
    {
        var span = leg.Origin.Value - leg.Terminus.Value;
        return span == 0m ? 0m : (level - leg.Terminus.Value) / span;
    }

    // An in-band array level: the entry price, the FVG it came from (null for an order block), and its formation
    // time (the deterministic last-resort tie-break, shared by FVGs and OBs).
    private readonly record struct Candidate(decimal Level, FairValueGap? Fvg, DateTimeOffset FormedAtUtc);

    private static List<FairValueGap> EligibleFvgs(
        MarketContext context, Direction direction, Timeframe timeframe, OteZone band)
    {
        var eligible = new List<FairValueGap>();
        foreach (var fvg in context.OpenFvgs)
        {
            if (fvg.IsOpen && fvg.Direction == direction && fvg.Timeframe == timeframe
                && band.Contains(new Price(fvg.Midpoint)))
            {
                eligible.Add(fvg);
            }
        }

        return eligible;
    }

    private static Candidate? SelectLevel(
        MarketContext context,
        Displacement leg,
        OteZone band,
        decimal sweetSpot,
        List<FairValueGap> eligibleFvgs,
        OteSelectionPolicy policy)
        => policy.StrictFirstFvg
            ? SelectShallowest(context, leg, band, sweetSpot, eligibleFvgs)
            : SelectNearestSweetSpot(context, leg.Direction, leg.Timeframe, band, sweetSpot, eligibleFvgs);

    // FVG-SEM-2a: the SHALLOWEST in-band array level (Argmin depth) = Ep3's "first higher fair value gap" — the
    // first level the retrace reaches. The eligible set is the SAME as the default path (in-band same-dir same-tf
    // FVGs AND order blocks); an OB can win the level (then no FVG is marked). Deterministic tie-break at equal
    // depth: nearest the 70.5% sweet spot, then earliest FormedAtUtc.
    private static Candidate? SelectShallowest(
        MarketContext context,
        Displacement leg,
        OteZone band,
        decimal sweetSpot,
        List<FairValueGap> eligibleFvgs)
    {
        Candidate? best = null;
        var bestDepth = decimal.MaxValue;
        var bestSweetSpotDistance = decimal.MaxValue;

        void Consider(decimal level, FairValueGap? fvg, DateTimeOffset formedAtUtc)
        {
            var depth = Depth(leg, level);
            var sweetSpotDistance = Math.Abs(level - sweetSpot);

            if (best is null
                || depth < bestDepth
                || (depth == bestDepth && sweetSpotDistance < bestSweetSpotDistance)
                || (depth == bestDepth && sweetSpotDistance == bestSweetSpotDistance && formedAtUtc < best.Value.FormedAtUtc))
            {
                best = new Candidate(level, fvg, formedAtUtc);
                bestDepth = depth;
                bestSweetSpotDistance = sweetSpotDistance;
            }
        }

        foreach (var fvg in eligibleFvgs)
        {
            Consider(fvg.Midpoint, fvg, fvg.FormedAtUtc);
        }

        foreach (var orderBlock in EligibleOrderBlocks(context, leg.Direction, leg.Timeframe, band))
        {
            Consider(orderBlock.Open.Value, null, orderBlock.FormedAtUtc);
        }

        return best;
    }

    // The existing nearest-the-sweet-spot pick (flag OFF) — byte-identical: FVGs first, then OBs, strict-less-than
    // so the first-seen at an equal distance wins.
    private static Candidate? SelectNearestSweetSpot(
        MarketContext context,
        Direction direction,
        Timeframe timeframe,
        OteZone band,
        decimal sweetSpot,
        List<FairValueGap> eligibleFvgs)
    {
        Candidate? best = null;
        var bestDistance = decimal.MaxValue;

        void Consider(decimal level, FairValueGap? fvg, DateTimeOffset formedAtUtc)
        {
            var distance = Math.Abs(level - sweetSpot);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new Candidate(level, fvg, formedAtUtc);
            }
        }

        foreach (var fvg in eligibleFvgs)
        {
            Consider(fvg.Midpoint, fvg, fvg.FormedAtUtc);
        }

        foreach (var orderBlock in EligibleOrderBlocks(context, direction, timeframe, band))
        {
            Consider(orderBlock.Open.Value, null, orderBlock.FormedAtUtc);
        }

        return best;
    }

    private static IEnumerable<OrderBlock> EligibleOrderBlocks(
        MarketContext context, Direction direction, Timeframe timeframe, OteZone band)
    {
        foreach (var orderBlock in context.OpenOrderBlocks)
        {
            if (orderBlock.IsOpen && orderBlock.Direction == direction && orderBlock.Timeframe == timeframe
                && band.Contains(new Price(orderBlock.Open.Value)))
            {
                yield return orderBlock;
            }
        }
    }

    // FVG-SEM-2a stacked DETECTION: the "farther" gap is the next-deeper same-dir same-tf OPEN FVG whose NEAR edge
    // is within StackProximityPips of the selected gap. If one exists, return its FAR edge (bullish: Bottom; bearish:
    // Top) — carried for FVG-SEM-2b; null otherwise. It deliberately scans the BROADER open set, NOT the band-filtered
    // eligible subset: the deeper stacked gap sits below a bullish entry (above a bearish one) and so its MIDPOINT is
    // usually OUTSIDE the 62–79% OTE band even when its near edge is within proximity (CodeRabbit #64).
    private static decimal? StackedFartherBound(
        MarketContext context,
        Direction direction,
        FairValueGap selected,
        decimal stackProximityPips)
    {
        var proximity = context.SymbolSpec.PipsToPrice(new Pips(stackProximityPips));
        var selectedDepthRef = direction == Direction.Bullish ? selected.Bottom.Value : selected.Top.Value;

        FairValueGap? farther = null;
        foreach (var fvg in context.OpenFvgs)
        {
            if (!fvg.IsOpen || fvg.Direction != direction || fvg.Timeframe != selected.Timeframe
                || ReferenceEquals(fvg, selected))
            {
                continue;
            }

            // Deeper-than-selected only (a stack is the gap BELOW a bullish entry / ABOVE a bearish entry).
            var isDeeper = direction == Direction.Bullish
                ? fvg.Midpoint < selected.Midpoint
                : fvg.Midpoint > selected.Midpoint;
            if (!isDeeper)
            {
                continue;
            }

            // Near edge of the deeper gap vs the selected gap's facing edge, within proximity.
            var nearEdge = direction == Direction.Bullish ? fvg.Top.Value : fvg.Bottom.Value;
            if (Math.Abs(selectedDepthRef - nearEdge) > proximity)
            {
                continue;
            }

            // The closest deeper gap (the immediate stack neighbour) is the "farther" one.
            if (farther is null
                || (direction == Direction.Bullish ? fvg.Midpoint > farther.Midpoint : fvg.Midpoint < farther.Midpoint))
            {
                farther = fvg;
            }
        }

        return farther is { } neighbour
            ? (direction == Direction.Bullish ? neighbour.Bottom.Value : neighbour.Top.Value)
            : null;
    }
}
