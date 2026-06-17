using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects a liquidity sweep (plan §2.5.1 step 4): price WICKS beyond an untapped pool by at least the
/// minimum penetration and then CLOSES back inside the pool — the close-back-inside is the sweep-vs-run
/// discriminator (a close BEYOND is a high-resistance run, HRLR, do not fade). Emits
/// <see cref="ConfluenceCondition.LiquiditySweep"/> in the direction the swept side enables (sweeping
/// buy-side ⇒ bearish). The premium/discount Judas read is applied to the PENETRATION (the swept wick
/// crossing the midnight reference open), independent of where the bar closes.
/// </summary>
public sealed class LiquiditySweepDetector : ISetupDetector
{
    private readonly LiquidityOptions _options;

    public LiquiditySweepDetector(LiquidityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.LiquiditySweep;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        var penetration = context.SymbolSpec.PipsToPrice(new Pips(_options.SweepMinPenetrationPips));

        foreach (var pool in context.LiquidityPools)
        {
            if (!pool.Untapped)
            {
                continue;
            }

            var result = pool.Side == LiquiditySide.BuySide
                ? EvaluateBuySide(context, current, pool, penetration)
                : EvaluateSellSide(context, current, pool, penetration);

            if (result is not null)
            {
                return result.Value;
            }
        }

        return DetectorResult.NoMatch;
    }

    private DetectorResult? EvaluateBuySide(MarketContext context, Candle current, LiquidityPool pool, decimal penetration)
    {
        if (current.High <= pool.Level.Value + penetration)
        {
            return null; // no penetrating wick (strict)
        }

        if (current.Close > pool.Level.Value)
        {
            pool.MarkRun(); // closed beyond -> run, not a sweep
            return null;
        }

        if (!(current.Close < pool.Level.Value))
        {
            return null; // closed exactly on the level: not a clean sweep
        }

        return Sweep(context, current, pool, Direction.Bearish, SwingKind.High, judasInPremium: true);
    }

    private DetectorResult? EvaluateSellSide(MarketContext context, Candle current, LiquidityPool pool, decimal penetration)
    {
        if (current.Low >= pool.Level.Value - penetration)
        {
            return null;
        }

        if (current.Close < pool.Level.Value)
        {
            pool.MarkRun();
            return null;
        }

        if (!(current.Close > pool.Level.Value))
        {
            return null;
        }

        return Sweep(context, current, pool, Direction.Bullish, SwingKind.Low, judasInPremium: false);
    }

    private DetectorResult Sweep(
        MarketContext context, Candle current, LiquidityPool pool, Direction direction, SwingKind sweptKind, bool judasInPremium)
    {
        pool.MarkSwept();
        ConsumeSweptSwing(context, sweptKind, pool.Level.Value);
        context.SetSweep(new SweepRecord(direction, pool.Level.Value, current.OpenTimeUtc, context.BarsProcessed));

        var isJudas = IsJudas(context, current, judasInPremium);
        var side = pool.Side;
        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.PoolLevel] = pool.Level.Value,
            [EvidenceKeys.SweptLevel] = pool.Level.Value,
            [EvidenceKeys.Direction] = direction.ToString(),
            [EvidenceKeys.IsJudas] = isJudas,
        };

        return DetectorResult.Match(
            direction, pool.Level.Value, ReasonFragments.LiquiditySwept(side, pool.Level.Value), evidence);
    }

    private void ConsumeSweptSwing(MarketContext context, SwingKind kind, decimal level)
    {
        var tolerance = context.SymbolSpec.PipsToPrice(new Pips(_options.EqualLevelTolerancePips));
        foreach (var swing in context.SwingPoints)
        {
            if (swing.IsActive && swing.Kind == kind && Math.Abs(swing.Price.Value - level) <= tolerance)
            {
                swing.MarkConsumed();
            }
        }
    }

    private static bool IsJudas(MarketContext context, Candle current, bool premium)
    {
        if (context.MidnightOpen is not { } open)
        {
            return true; // no reference open available yet
        }

        // Judas iff the swept wick traded on the correct side of the midnight open.
        return premium ? current.High > open : current.Low < open;
    }
}
