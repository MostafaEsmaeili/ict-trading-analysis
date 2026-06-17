using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.MarketStructure;

public enum LiquiditySide
{
    /// <summary>Buy-side liquidity above old/equal highs.</summary>
    BuySide,

    /// <summary>Sell-side liquidity below old/equal lows.</summary>
    SellSide,
}

public enum LiquidityConsumption
{
    None,

    /// <summary>Wick beyond the level then a close back inside — a sweep (tradeable raid).</summary>
    Swept,

    /// <summary>A close beyond the level — a high-resistance liquidity run (HRLR); do NOT fade (§2.5.8).</summary>
    Run,
}

/// <summary>
/// A pool of resting liquidity (plan §2.3/§2.5.1 steps 2 &amp; 4). Sweeping a buy-side pool (above) enables a
/// bearish trade; a sell-side pool (below) enables bullish. It distinguishes a SWEEP (wick + close back
/// inside) from a RUN (close beyond ⇒ HRLR, do-not-fade), which is the sweep-vs-run discriminator.
/// </summary>
public sealed class LiquidityPool
{
    public LiquidityPool(LiquiditySide side, Price level, int strength, DateTimeOffset formedAtUtc)
    {
        Guard.Against(strength < 1, "LiquidityPool strength must be at least 1.");
        Guard.Against(formedAtUtc.Offset != TimeSpan.Zero, "LiquidityPool.FormedAtUtc must be UTC.");
        Side = side;
        Level = level;
        Strength = strength;
        FormedAtUtc = formedAtUtc;
    }

    public LiquiditySide Side { get; }

    public Price Level { get; }

    /// <summary>How many equal touches formed the pool — a relative-equal cluster is stronger.</summary>
    public int Strength { get; private set; }

    public DateTimeOffset FormedAtUtc { get; }

    public bool Untapped { get; private set; } = true;

    public LiquidityConsumption Consumption { get; private set; } = LiquidityConsumption.None;

    public Direction EnablesDirection => Side == LiquiditySide.BuySide ? Direction.Bearish : Direction.Bullish;

    public void IncreaseStrength() => Strength++;

    /// <summary>The pool was swept (wick beyond + close back inside) — a tradeable raid.</summary>
    public void MarkSwept()
    {
        Untapped = false;
        Consumption = LiquidityConsumption.Swept;
    }

    /// <summary>The pool was run through (close beyond) — HRLR, do not fade.</summary>
    public void MarkRun()
    {
        Untapped = false;
        Consumption = LiquidityConsumption.Run;
    }
}
