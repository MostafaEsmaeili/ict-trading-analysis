using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Registers pools of resting liquidity (plan §2.5.1 step 2) — a feeder with no confluence. For WP1 the
/// pools mirror the active swing registry (buy-side above swing highs, sell-side below swing lows), deduped
/// within the equal-level tolerance so relative-equal levels collapse to one pool. Prior-day H/L and
/// big-figure pools are a later addition.
/// </summary>
public sealed class LiquidityPoolDetector : ISetupDetector
{
    private readonly LiquidityOptions _options;

    public LiquidityPoolDetector(LiquidityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => null;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tolerance = context.SymbolSpec.PipsToPrice(new Pips(_options.EqualLevelTolerancePips));

        foreach (var swing in context.SwingPoints)
        {
            if (!swing.IsActive)
            {
                continue;
            }

            var side = swing.Kind == SwingKind.High ? LiquiditySide.BuySide : LiquiditySide.SellSide;
            if (!HasPoolNear(context, side, swing.Price.Value, tolerance))
            {
                context.RegisterLiquidityPool(new LiquidityPool(side, swing.Price, 1, swing.FormedAtUtc));
            }
        }

        return DetectorResult.NoMatch;
    }

    private static bool HasPoolNear(MarketContext context, LiquiditySide side, decimal level, decimal tolerance)
        => context.LiquidityPools.Any(pool => pool.Side == side && Math.Abs(pool.Level.Value - level) <= tolerance);
}
