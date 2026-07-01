using IctTrader.Domain.Detection;
using IctTrader.Domain.MarketStructure;
using IctTrader.Scanning.Contracts;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// Projects the scanner's live <see cref="MarketContext"/> working memory into the flat, kind-discriminated
/// <see cref="GeometryOverlayDto"/> list the ICT Pattern Chart draws as its "engine view" (plan §9.1) — the concepts
/// the scanner is tracking RIGHT NOW (open FVGs / order blocks / liquidity pools, the latest sweep / MSS, and the OTE
/// band of the latest displacement leg), so the operator can SEE what is active even between confirmed setups.
///
/// <para>PURE + read-only: it only reads the context's registries and derives prices; it mutates nothing and routes
/// nowhere near an order path (plan §6.3 guardrail). Called on the scan thread (sequential per bus dispatch), it
/// builds an IMMUTABLE snapshot the store hands to the HTTP read side tear-free.</para>
///
/// <para><b>Capped per concept</b> (the operator's declutter concern): FVG/OB/liquidity are the many-instance layers,
/// so only the most-recent handful is kept (liquidity prefers still-untapped pools — the live draw targets). Sweep /
/// MSS / OTE are single "latest" records. Caps are named consts, not magic numbers.</para>
/// </summary>
public static class GeometryOverlayMapper
{
    /// <summary>The most-recent open FVGs to surface (newest first).</summary>
    public const int MaxFvgs = 6;

    /// <summary>The most-recent open order blocks to surface (newest first).</summary>
    public const int MaxOrderBlocks = 4;

    /// <summary>The liquidity pools to surface — untapped (live draw targets) prioritised, then recent swept/run.</summary>
    public const int MaxLiquidityPools = 10;

    /// <summary>
    /// Snapshots <paramref name="context"/> into overlay DTOs. <paramref name="orderBlockMeanPercent"/> is the OB body
    /// mean fraction (the §2.5 50% entry threshold, from the resolved <c>OrderBlockOptions</c>); the OTE fibs
    /// (<paramref name="oteLowerFib"/> / <paramref name="oteUpperFib"/> / <paramref name="oteSweetSpotFib"/>) are the
    /// resolved <c>OteOptions</c> band — the band is derived off the SAME <see cref="Displacement.Project"/> axis the
    /// OTE detector uses, so the drawn zone can never drift from the entry logic.
    /// </summary>
    public static IReadOnlyList<GeometryOverlayDto> Snapshot(
        MarketContext context,
        decimal orderBlockMeanPercent,
        decimal oteLowerFib,
        decimal oteUpperFib,
        decimal oteSweetSpotFib)
    {
        ArgumentNullException.ThrowIfNull(context);

        var overlays = new List<GeometryOverlayDto>();

        // Open FVGs — a translucent box per gap (Top/Bottom); State greys a mitigated gap on the client.
        foreach (var fvg in Recent(context.OpenFvgs, MaxFvgs))
        {
            overlays.Add(new GeometryOverlayDto(
                Kind: "fvg",
                Direction: fvg.Direction.ToString(),
                AtUtc: fvg.FormedAtUtc,
                Top: fvg.Top.Value,
                Bottom: fvg.Bottom.Value,
                State: fvg.State.ToString()));
        }

        // Open order blocks — a box (High/Low) + the 50% mean-threshold entry line (Mid).
        foreach (var ob in Recent(context.OpenOrderBlocks, MaxOrderBlocks))
        {
            overlays.Add(new GeometryOverlayDto(
                Kind: "orderBlock",
                Direction: ob.Direction.ToString(),
                AtUtc: ob.FormedAtUtc,
                Top: ob.High.Value,
                Bottom: ob.Low.Value,
                Mid: ob.MeanThreshold(orderBlockMeanPercent),
                State: ob.State.ToString()));
        }

        // Resting liquidity pools — a tagged buy-/sell-side line; still-untapped pools are the live draw targets.
        foreach (var pool in RecentLiquidity(context.LiquidityPools, MaxLiquidityPools))
        {
            overlays.Add(new GeometryOverlayDto(
                Kind: "liquidity",
                Direction: pool.EnablesDirection.ToString(),
                AtUtc: pool.FormedAtUtc,
                Price: pool.Level.Value,
                State: pool.Consumption.ToString(),
                Side: pool.Side.ToString(),
                Swept: !pool.Untapped,
                Strength: pool.Strength));
        }

        // The latest liquidity sweep (the Judas raid) — a point marker at the swept level.
        if (context.LastSweep is { } sweep)
        {
            overlays.Add(new GeometryOverlayDto(
                Kind: "sweep",
                Direction: sweep.Direction.ToString(),
                AtUtc: sweep.AtUtc,
                Price: sweep.Level));
        }

        // The latest confirmed market-structure shift — a marker at the broken swing.
        if (context.LastMss is { } mss)
        {
            overlays.Add(new GeometryOverlayDto(
                Kind: "mss",
                Direction: mss.Direction.ToString(),
                AtUtc: mss.AtUtc,
                Price: mss.BrokenSwingLevel.Value));
        }

        // The OTE band of the latest displacement leg — the 62% / 79% retrace prices + the 70.5% sweet spot, off the
        // leg's own Project(fraction) axis (0 = terminus, 1 = origin). Top/Bottom carry the two band edges as-computed
        // (their raw 62%/79% prices — the client labels them, not their relative order), Mid carries the sweet spot.
        if (context.LastDisplacement is { } leg)
        {
            overlays.Add(new GeometryOverlayDto(
                Kind: "ote",
                Direction: leg.Direction.ToString(),
                AtUtc: leg.OriginAtUtc,
                Top: leg.Project(oteLowerFib),
                Bottom: leg.Project(oteUpperFib),
                Mid: leg.Project(oteSweetSpotFib)));
        }

        return overlays;
    }

    /// <summary>The last <paramref name="max"/> items of a formation-ordered (oldest-first) registry, NEWEST-FIRST.</summary>
    private static List<T> Recent<T>(IReadOnlyList<T> items, int max)
    {
        var take = Math.Min(max, items.Count);
        var result = new List<T>(take);
        for (var i = 0; i < take; i++)
        {
            result.Add(items[items.Count - 1 - i]);
        }

        return result;
    }

    /// <summary>
    /// Up to <paramref name="max"/> pools, UNTAPPED first (still-drawing targets), then the most-recent consumed ones
    /// for context — each group newest-first (the registry is oldest-first, so walk it backward).
    /// </summary>
    private static List<LiquidityPool> RecentLiquidity(IReadOnlyList<LiquidityPool> pools, int max)
    {
        var untapped = new List<LiquidityPool>();
        var consumed = new List<LiquidityPool>();
        for (var i = pools.Count - 1; i >= 0; i--)
        {
            (pools[i].Untapped ? untapped : consumed).Add(pools[i]);
        }

        var result = new List<LiquidityPool>(Math.Min(max, pools.Count));
        foreach (var pool in untapped)
        {
            if (result.Count >= max)
            {
                return result;
            }

            result.Add(pool);
        }

        foreach (var pool in consumed)
        {
            if (result.Count >= max)
            {
                break;
            }

            result.Add(pool);
        }

        return result;
    }
}
