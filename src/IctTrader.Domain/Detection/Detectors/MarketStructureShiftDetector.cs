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

        // The current candle must be the energetic displacement (consumed from the displacement detector).
        if (context.LastDisplacement is not { } displacement || displacement.AtUtc != current.OpenTimeUtc)
        {
            return DetectorResult.NoMatch;
        }

        var direction = displacement.Direction;

        if (_options.RequirePrecedentSweep && !HasPrecedentSweep(context, direction))
        {
            return DetectorResult.NoMatch; // sweep must precede the shift
        }

        var brokenSwing = FindBrokenSwing(context, direction, current);
        if (brokenSwing is null)
        {
            return DetectorResult.NoMatch;
        }

        var beyondPips = direction == Direction.Bullish
            ? context.SymbolSpec.PriceToPips(current.Close - brokenSwing.Price.Value)
            : context.SymbolSpec.PriceToPips(brokenSwing.Price.Value - current.Close);

        if (beyondPips.Value < _options.CloseBeyondMinPips)
        {
            return DetectorResult.NoMatch; // weak / wick-only break
        }

        brokenSwing.Breach(current.OpenTimeUtc);
        var shift = new MarketStructureShift(
            direction, current.Timeframe, brokenSwing.Price, new Price(current.Close), current.OpenTimeUtc);
        context.SetMarketStructureShift(shift);

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = direction.ToString(),
            [EvidenceKeys.BrokenSwingPrice] = brokenSwing.Price.Value,
            [EvidenceKeys.Timeframe] = current.Timeframe.ToString(),
        };

        return DetectorResult.Match(
            direction,
            current.Close,
            ReasonFragments.MarketStructureShift(direction, brokenSwing.Price.Value, current.Timeframe),
            evidence);
    }

    private bool HasPrecedentSweep(MarketContext context, Direction direction)
        => context.LastSweep is { } sweep
            && sweep.Direction == direction
            && context.BarsProcessed - sweep.BarIndex <= _options.SweepToMssMaxBars;

    private static SwingPoint? FindBrokenSwing(MarketContext context, Direction direction, Candle current)
    {
        var kind = direction == Direction.Bullish ? SwingKind.High : SwingKind.Low;
        SwingPoint? nearest = null;

        foreach (var swing in context.SwingPoints)
        {
            if (swing.Kind != kind)
            {
                continue;
            }

            // The swing the displacement candle CLOSES THROUGH is the MSS reference. Accept a swing that is
            // still live OR was breached by THIS candle (so the order in which SwingPointDetector and this
            // detector run cannot drop a legitimate MSS — spec §5 item 19). A swing consumed by a sweep, or
            // breached on an EARLIER bar, is stale structure and excluded.
            if (!swing.IsActive && !swing.WasBreachedOn(current.OpenTimeUtc))
            {
                continue;
            }

            var broken = direction == Direction.Bullish
                ? current.Close > swing.Price.Value
                : current.Close < swing.Price.Value;
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
