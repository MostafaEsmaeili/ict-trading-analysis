using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects a market-structure shift (plan §2.5.1 step 5) and emits the SINGLE
/// <see cref="ConfluenceCondition.DisplacementMss"/> (0.95) — the displacement is a non-scoring precondition,
/// so the weight is counted once here. The current candle must be an energetic displacement that, AFTER a
/// precedent sweep in the same direction (within the bar window), CLOSES beyond a prior swing by at least
/// the minimum (a weak/wick-only break is rejected). It breaches the broken swing and records the shift.
/// </summary>
public sealed class MarketStructureShiftDetector : ISetupDetector
{
    private readonly MarketStructureShiftOptions _options;

    public MarketStructureShiftDetector(MarketStructureShiftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.DisplacementMss;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        InvalidateBrokenMss(context, current);

        // The leg must be published on THIS (its terminus/birth) bar — consume it the same bar (the detector
        // run-order pins SwingPointDetector → DisplacementDetector → this).
        if (context.LastDisplacement is not { } leg || leg.Retraced || leg.AtUtc != current.OpenTimeUtc)
        {
            return DetectorResult.NoMatch;
        }

        var direction = leg.Direction;

        // Reconstruct the leg members from the window + the VO span (no shared mutable state). A window pruned
        // below the span is a fail-safe NoMatch.
        var window = context.Window(leg.Timeframe);
        var members = new List<Candle>(leg.LegBars);
        foreach (var candle in window)
        {
            if (candle.OpenTimeUtc >= leg.OriginAtUtc && candle.OpenTimeUtc <= leg.AtUtc)
            {
                members.Add(candle);
            }
        }

        if (members.Count != leg.LegBars)
        {
            return DetectorResult.NoMatch;
        }

        // Member-scan at leg-birth (TIME-11-12): the EARLIEST member that closes beyond a prior swing by at least
        // the minimum is the shift (Ep25:327 — the first break IS the shift). Selection stops at that member; the
        // precedent-sweep is then measured ONCE to it (it does NOT fall through to a later breaking member).
        var lastMemberIdx = window.Count - 1; // terminus == current
        var kind = direction == Direction.Bullish ? SwingKind.High : SwingKind.Low;

        Candle? breakingMember = null;
        SwingPoint? brokenSwing = null;
        var breakingMemberIdx = 0;
        for (var m = 0; m < members.Count; m++)
        {
            var member = members[m];
            var swing = FindBrokenSwing(context, kind, direction, member);
            if (swing is null)
            {
                continue;
            }

            var beyondPips = direction == Direction.Bullish
                ? context.SymbolSpec.PriceToPips(member.Close - swing.Price.Value)
                : context.SymbolSpec.PriceToPips(swing.Price.Value - member.Close);

            if (beyondPips.Value < _options.CloseBeyondMinPips)
            {
                continue; // weak / wick-only break
            }

            breakingMember = member;
            brokenSwing = swing;
            breakingMemberIdx = m;
            break; // the EARLIEST member that breaks
        }

        if (breakingMember is not { } breaking || brokenSwing is null)
        {
            return DetectorResult.NoMatch;
        }

        // The breaking member's bar index — the terminus is at BarsProcessed, each earlier member one less. The
        // sweep must STRICTLY precede THIS member (the break can be up to LegBars-1 bars before the terminus).
        // e.g. a 3-bar leg whose interior bar2 breaks, at BarsProcessed=3: memberWindowIdx=1, breakingMemberBarIndex=2.
        var memberWindowIdx = lastMemberIdx - (members.Count - 1 - breakingMemberIdx);
        var breakingMemberBarIndex = context.BarsProcessed - (lastMemberIdx - memberWindowIdx);
        if (_options.RequirePrecedentSweep && !HasPrecedentSweep(context, direction, breakingMemberBarIndex))
        {
            return DetectorResult.NoMatch; // sweep must precede the shift
        }

        brokenSwing.Breach(breaking.OpenTimeUtc);
        var shift = new MarketStructureShift(
            direction, current.Timeframe, brokenSwing.Price, new Price(breaking.Close), breaking.OpenTimeUtc);
        context.SetMarketStructureShift(shift);

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = direction.ToString(),
            [EvidenceKeys.BrokenSwingPrice] = brokenSwing.Price.Value,
            [EvidenceKeys.Timeframe] = current.Timeframe.ToString(),
        };

        return DetectorResult.Match(
            direction,
            breaking.Close,
            ReasonFragments.MarketStructureShift(direction, brokenSwing.Price.Value, current.Timeframe),
            evidence);
    }

    private bool HasPrecedentSweep(MarketContext context, Direction direction, long breakingMemberBarIndex)
        => context.LastSweep is { } sweep
            && sweep.Direction == direction
            && sweep.BarIndex < breakingMemberBarIndex
            && breakingMemberBarIndex - sweep.BarIndex <= _options.SweepToMssMaxBars;

    private static SwingPoint? FindBrokenSwing(MarketContext context, SwingKind kind, Direction direction, Candle member)
    {
        SwingPoint? nearest = null;

        foreach (var swing in context.SwingPoints)
        {
            if (swing.Kind != kind)
            {
                continue;
            }

            // The swing the member candle CLOSES THROUGH is the MSS reference. Accept a swing that is still live
            // OR was breached by THIS member (so the order in which SwingPointDetector and this detector run cannot
            // drop a legitimate MSS — spec §5 item 19). A swing consumed by a sweep, or breached on an EARLIER bar,
            // is stale structure and excluded.
            if (!swing.IsActive && !swing.WasBreachedOn(member.OpenTimeUtc))
            {
                continue;
            }

            // Causality: a swing cannot be broken by a member that closed before it formed (no-op at length 1).
            if (swing.FormedAtUtc > member.OpenTimeUtc)
            {
                continue;
            }

            var broken = direction == Direction.Bullish
                ? member.Close > swing.Price.Value
                : member.Close < swing.Price.Value;
            if (!broken)
            {
                continue;
            }

            nearest = nearest is null
                ? swing
                : Nearer(direction, swing, nearest);
        }

        return nearest;
    }

    // The nearest broken swing is the highest swing-high below the close (or the lowest swing-low above it).
    private static SwingPoint Nearer(Direction direction, SwingPoint candidate, SwingPoint current)
        => direction == Direction.Bullish
            ? candidate.Price.Value > current.Price.Value ? candidate : current
            : candidate.Price.Value < current.Price.Value ? candidate : current;

    private static void InvalidateBrokenMss(MarketContext context, Candle current)
    {
        if (context.LastMss is not { IsConfirmed: true } mss)
        {
            return;
        }

        // Invalidation: price closes back beyond the broken swing, against the shift direction.
        var breached = mss.Direction == Direction.Bullish
            ? current.Close < mss.BrokenSwingLevel.Value
            : current.Close > mss.BrokenSwingLevel.Value;

        if (breached)
        {
            mss.Invalidate();
        }
    }
}
