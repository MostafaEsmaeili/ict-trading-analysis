using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects an order block (plan §2.5.1 step 6) and emits <see cref="ConfluenceCondition.OrderBlockConfluence"/>
/// (0.65, not required). The OB is the last opposite-close candle before the displacement; its opening price
/// is the key level. A valid OB REQUIRES a linked FVG in the same direction, and its open must sit in the
/// correct premium/discount half — mirroring the verifier-corrected FVG operators (bullish OB in discount,
/// bearish in premium).
/// </summary>
public sealed class OrderBlockDetector : ISetupDetector
{
    private readonly OrderBlockOptions _options;

    public OrderBlockDetector(OrderBlockOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
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

        var origin = FindLastOppositeCloseCandle(context, direction, current);
        if (origin is not { } block)
        {
            return DetectorResult.NoMatch;
        }

        if (_options.RequireInCorrectHalf && !InCorrectHalf(direction, block.Open, displacement.EquilibriumPrice))
        {
            return DetectorResult.NoMatch;
        }

        var orderBlock = new OrderBlock(
            direction, current.Timeframe, new Price(block.Open), new Price(block.High), new Price(block.Low), block.OpenTimeUtc);
        context.RegisterOrderBlock(orderBlock);

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.OpeningPrice] = block.Open,
            [EvidenceKeys.MeanThreshold] = orderBlock.MeanThreshold(_options.MeanThresholdPercent),
            [EvidenceKeys.Direction] = direction.ToString(),
        };

        return DetectorResult.Match(
            direction, block.Open, ReasonFragments.OrderBlock(direction, block.Open, current.Timeframe), evidence);
    }

    // The linked FVG must belong to the same displacement leg. As a §2.5.7-deferred approximation we require
    // same direction AND (when RequireSameTimeframeFvg) the OB's own timeframe, so a stale same-direction gap
    // from another leg/timeframe cannot manufacture false confluence. Precise bar-window leg linkage (which
    // permits the §2.5 step-6 15m→1m finer-TF entry FVG) is owned by the SetupCandidate FSM in WP3.
    private bool HasLinkedFvg(MarketContext context, Direction direction, Timeframe timeframe)
        => context.OpenFvgs.Any(fvg => fvg.IsOpen
            && fvg.Direction == direction
            && (!_options.RequireSameTimeframeFvg || fvg.Timeframe == timeframe));

    // For a bullish displacement the OB is the last DOWN-close candle before it; mirror for bearish.
    private static Candle? FindLastOppositeCloseCandle(MarketContext context, Direction direction, Candle current)
    {
        var window = context.Window(current.Timeframe);
        for (var i = window.Count - 2; i >= 0; i--)
        {
            var candle = window[i];
            var opposite = direction == Direction.Bullish ? candle.IsDownClose : candle.IsUpClose;
            if (opposite)
            {
                return candle;
            }
        }

        return null;
    }

    private static bool InCorrectHalf(Direction direction, decimal openingPrice, decimal equilibrium)
        => direction == Direction.Bullish ? openingPrice <= equilibrium : openingPrice >= equilibrium;
}
