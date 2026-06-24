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
/// An order block (plan §2.3/§2.5.1 step 6, decision OB-9a): the CONSECUTIVE opposite-close run before a
/// displacement, anchored at the candle that STARTS the run. The key level is the anchor's opening price;
/// the zone <see cref="High"/>/<see cref="Low"/> span the whole cluster; the mean-threshold (default 50%)
/// is the BODY midpoint of the anchor candle (Ep13/Ep19 — NOT the zone range). A valid OB REQUIRES a linked
/// FVG — that gating is enforced by the detector, this type carries the array + lifecycle (mitigation;
/// inversion to a breaker on a break of structure, §2.5.10).
/// </summary>
public sealed class OrderBlock
{
    public OrderBlock(
        Direction direction,
        Timeframe timeframe,
        Price open,
        Price high,
        Price low,
        Price bodyLow,
        Price bodyHigh,
        DateTimeOffset formedAtUtc)
    {
        Guard.Against(high.Value < low.Value, "OrderBlock high must be >= low.");
        Guard.Against(open.Value < low.Value || open.Value > high.Value, "OrderBlock open must lie within its range.");
        Guard.Against(bodyHigh.Value < bodyLow.Value, "OrderBlock bodyHigh must be >= bodyLow.");
        Guard.Against(bodyLow.Value < low.Value || bodyHigh.Value > high.Value, "OrderBlock body must lie within its range.");
        Guard.Against(open.Value < bodyLow.Value || open.Value > bodyHigh.Value, "OrderBlock open must be a body extreme.");
        Guard.Against(formedAtUtc.Offset != TimeSpan.Zero, "OrderBlock.FormedAtUtc must be UTC.");
        Direction = direction;
        Timeframe = timeframe;
        Open = open;
        High = high;
        Low = low;
        BodyLow = bodyLow;
        BodyHigh = bodyHigh;
        FormedAtUtc = formedAtUtc;
    }

    public Direction Direction { get; }

    public Timeframe Timeframe { get; }

    /// <summary>The key entry level — the order block's (anchor candle's) opening price.</summary>
    public Price Open { get; }

    /// <summary>The cluster-span high (max High across the consecutive run).</summary>
    public Price High { get; }

    /// <summary>The cluster-span low (min Low across the consecutive run).</summary>
    public Price Low { get; }

    /// <summary>The anchor candle's body low (= min(Open, Close)) — the basis of the mean-threshold.</summary>
    public Price BodyLow { get; }

    /// <summary>The anchor candle's body high (= max(Open, Close)) — the basis of the mean-threshold.</summary>
    public Price BodyHigh { get; }

    public DateTimeOffset FormedAtUtc { get; }

    public OrderBlockState State { get; private set; } = OrderBlockState.Open;

    public bool IsOpen => State == OrderBlockState.Open;

    /// <summary>
    /// The mean-threshold price at the given fraction of the anchor candle's BODY (default 0.50). At 0.50
    /// this is the body midpoint (anchor.Open + anchor.Close) / 2 — Ep13:207, Ep19:724 — NOT the zone range.
    /// </summary>
    public decimal MeanThreshold(decimal meanPercent)
    {
        Guard.Against(meanPercent is < 0m or > 1m, "OrderBlock meanPercent must be within [0, 1].");
        return BodyLow.Value + ((BodyHigh.Value - BodyLow.Value) * meanPercent);
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
