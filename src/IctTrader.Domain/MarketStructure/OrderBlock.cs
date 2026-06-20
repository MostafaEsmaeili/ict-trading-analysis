using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.MarketStructure;

public enum OrderBlockState
{
    Open,
    Mitigated,
    Inverted,
}

/// <summary>
/// An order block (plan §2.3/§2.5.1 step 6): the last opposite-close candle before a displacement. The key
/// level is its opening price; the mean-threshold (default 50%) is a partial/entry reference. A valid OB
/// REQUIRES a linked FVG — that gating is enforced by the detector, this type carries the array + lifecycle
/// (mitigation; inversion to a breaker on a break of structure, §2.5.10).
/// </summary>
public sealed class OrderBlock
{
    public OrderBlock(Direction direction, Timeframe timeframe, Price open, Price high, Price low, DateTimeOffset formedAtUtc)
    {
        Guard.Against(high.Value < low.Value, "OrderBlock high must be >= low.");
        Guard.Against(open.Value < low.Value || open.Value > high.Value, "OrderBlock open must lie within its range.");
        Guard.Against(formedAtUtc.Offset != TimeSpan.Zero, "OrderBlock.FormedAtUtc must be UTC.");
        Direction = direction;
        Timeframe = timeframe;
        Open = open;
        High = high;
        Low = low;
        FormedAtUtc = formedAtUtc;
    }

    public Direction Direction { get; }

    public Timeframe Timeframe { get; }

    /// <summary>The key entry level — the order block's opening price.</summary>
    public Price Open { get; }

    public Price High { get; }

    public Price Low { get; }

    public DateTimeOffset FormedAtUtc { get; }

    public OrderBlockState State { get; private set; } = OrderBlockState.Open;

    public bool IsOpen => State == OrderBlockState.Open;

    /// <summary>The mean-threshold price at the given fraction of the block's range (default 0.50).</summary>
    public decimal MeanThreshold(decimal meanPercent)
    {
        Guard.Against(meanPercent is < 0m or > 1m, "OrderBlock meanPercent must be within [0, 1].");
        return Low.Value + ((High.Value - Low.Value) * meanPercent);
    }

    /// <summary>Invalidation: price closed through beyond the open, or the linked FVG mitigated.</summary>
    public void Mitigate()
    {
        if (IsOpen)
        {
            State = OrderBlockState.Mitigated;
        }
    }

    /// <summary>PD-array inversion on a break of structure (§2.5.10): the OB becomes a breaker.</summary>
    public void Invert() => State = OrderBlockState.Inverted;
}
