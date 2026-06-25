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

        // A close strictly BEYOND the high is a run/HRLR (§2.5.8) — consume the pool and do not fade.
        if (current.Close > pool.Level.Value)
        {
            pool.MarkRun();
            return null;
        }

        // A close exactly ON the level has no rejection body, so it is not a clean sweep — but the resting
        // liquidity was never run THROUGH, so the pool stays UNTAPPED for a genuine later sweep (§2.5.1 step 4).
        if (current.Close == pool.Level.Value)
        {
            return null;
        }

        return Sweep(context, current, pool, Direction.Bearish, SwingKind.High, judasInPremium: true);
    }

    private DetectorResult? EvaluateSellSide(MarketContext context, Candle current, LiquidityPool pool, decimal penetration)
    {
        if (current.Low >= pool.Level.Value - penetration)
        {
            return null;
        }

        // Mirror of the buy-side: a close strictly below the low is a run (consume); a close exactly ON the
        // level is the boundary — not a sweep, but the pool stays untapped for a genuine later sweep.
        if (current.Close < pool.Level.Value)
        {
            pool.MarkRun();
            return null;
        }

        if (current.Close == pool.Level.Value)
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
        // The reference open is midnight by default; with the macro-open reference enabled it is the
        // lower/higher of midnight and the 08:30 macro open per the bearish/bullish read (TIME-10).
        if (context.ReferenceOpen(premium) is not { } open)
        {
            return true; // no reference open available yet
        }

        // Judas iff the swept wick traded on the correct side of the reference open.
        return premium ? current.High > open : current.Low < open;
    }
}
