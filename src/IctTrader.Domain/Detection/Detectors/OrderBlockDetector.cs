using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects an order block (plan §2.5.1 step 6, decision OB-9a) and emits
/// <see cref="ConfluenceCondition.OrderBlockConfluence"/> (0.65, not required). The OB is the CONSECUTIVE
/// opposite-close run before the displacement, anchored at the candle that STARTS the run; its opening price
/// is the key level, its zone spans the whole cluster, and its mean-threshold is that anchor candle's BODY
/// midpoint. A valid OB REQUIRES a linked FVG in the same direction, and the anchor open must sit in the
/// correct premium/discount half — mirroring the verifier-corrected FVG operators (bullish OB in discount,
/// bearish in premium).
/// </summary>
public sealed class OrderBlockDetector : ISetupDetector
{
    private readonly OrderBlockOptions _options;

    public OrderBlockDetector(OrderBlockOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Fail fast on an invalid cluster cap: it is an index-arithmetic bound, so a value < 1 would push the run
        // start past the displacement candle (wrong anchor / out-of-range). Host ValidateOnStart also rejects it.
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxClusterCandles, 1);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.OrderBlockConfluence;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.LastDisplacement is not { } displacement || displacement.AtUtc != current.OpenTimeUtc)
        {
            return DetectorResult.NoMatch;
        }

        var direction = displacement.Direction;

        if (_options.RequireFvg && !HasLinkedFvg(context, direction, current.Timeframe))
        {
            return DetectorResult.NoMatch; // an OB without its (leg-linked) FVG is invalid
        }

        var cluster = FindOppositeCloseCluster(context, direction, current);
        if (cluster is not { } c)
        {
            return DetectorResult.NoMatch;
        }

        var anchor = c.Anchor;

        if (_options.RequireInCorrectHalf && !InCorrectHalf(direction, anchor.Open, displacement.EquilibriumPrice))
        {
            return DetectorResult.NoMatch;
        }

        var bodyLow = Math.Min(anchor.Open, anchor.Close);
        var bodyHigh = Math.Max(anchor.Open, anchor.Close);
        var orderBlock = new OrderBlock(
            direction,
            current.Timeframe,
            new Price(anchor.Open),
            new Price(c.ZoneHigh),
            new Price(c.ZoneLow),
            new Price(bodyLow),
            new Price(bodyHigh),
            anchor.OpenTimeUtc);
        context.RegisterOrderBlock(orderBlock);

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.OpeningPrice] = anchor.Open,
            [EvidenceKeys.MeanThreshold] = orderBlock.MeanThreshold(_options.MeanThresholdPercent),
            [EvidenceKeys.Direction] = direction.ToString(),
        };

        return DetectorResult.Match(
            direction, anchor.Open, ReasonFragments.OrderBlock(direction, anchor.Open, current.Timeframe), evidence);
    }

    // The linked FVG must belong to the same displacement leg. As a §2.5.7-deferred approximation we require
    // same direction AND (when RequireSameTimeframeFvg) the OB's own timeframe, so a stale same-direction gap
    // from another leg/timeframe cannot manufacture false confluence. Precise bar-window leg linkage (which
    // permits the §2.5 step-6 15m→1m finer-TF entry FVG) is owned by the SetupCandidate FSM in WP3.
    private bool HasLinkedFvg(MarketContext context, Direction direction, Timeframe timeframe)
        => context.OpenFvgs.Any(fvg => fvg.IsOpen
            && fvg.Direction == direction
            && (!_options.RequireSameTimeframeFvg || fvg.Timeframe == timeframe));

    // The OB is the CONSECUTIVE opposite-close run ending at the bar immediately before the displacement
    // (OB-9a). For a bullish displacement those are DOWN-close candles; mirror for bearish. A doji (neither
    // up nor down close) terminates the run. The anchor is the EARLIEST candle of the retained run (capped at
    // MaxClusterCandles counted back from the displacement); the zone spans the run's High/Low extremes.
    // Reads the SAME timeframe window the displacement is on — true displacement-leg bar-window membership
    // (the §2.5.1 step-6 15m→1m finer-TF entry) stays the SetupCandidate FSM's job (WP3).
    private (Candle Anchor, decimal ZoneHigh, decimal ZoneLow)? FindOppositeCloseCluster(
        MarketContext context, Direction direction, Candle current)
    {
        var window = context.Window(current.Timeframe);

        // The run ENDS at the bar immediately before the displacement candle (window[^1] == current).
        var runEnd = window.Count - 2;
        if (runEnd < 0)
        {
            return null; // no candle precedes the displacement
        }

        // The run must END at an opposite-close bar adjacent to the displacement; we do NOT search backwards
        // past a with-close bar to find an older run (preserves the legacy no-opposite-close => NoMatch).
        if (!IsOppositeClose(window[runEnd], direction))
        {
            return null;
        }

        var runStart = runEnd;
        while (runStart - 1 >= 0 && IsOppositeClose(window[runStart - 1], direction))
        {
            runStart--;
        }

        // Cap the run at MaxClusterCandles counted from the END (the displacement side).
        var cappedStart = runEnd - _options.MaxClusterCandles + 1;
        if (runStart < cappedStart)
        {
            runStart = cappedStart;
        }

        var anchor = window[runStart];
        var zoneHigh = anchor.High;
        var zoneLow = anchor.Low;
        for (var i = runStart + 1; i <= runEnd; i++)
        {
            zoneHigh = Math.Max(zoneHigh, window[i].High);
            zoneLow = Math.Min(zoneLow, window[i].Low);
        }

        return (anchor, zoneHigh, zoneLow);
    }

    // Opposite-close = closed against the displacement direction. A doji (Close == Open) is NEITHER an
    // up- nor a down-close, so it is NOT an opposite-close candle and terminates the run.
    private static bool IsOppositeClose(Candle candle, Direction direction)
        => direction == Direction.Bullish ? candle.IsDownClose : candle.IsUpClose;

    private static bool InCorrectHalf(Direction direction, decimal openingPrice, decimal equilibrium)
        => direction == Direction.Bullish ? openingPrice <= equilibrium : openingPrice >= equilibrium;
}
