using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.MarketStructure;

/// <summary>
/// The dealing range between two broken swings (plan §2.4/§2.5.1 step 1) used for the premium/discount
/// read. Anchored body-to-body on the broken swing by default; re-anchored when a new swing breaks
/// (invalidation on expansion). The equilibrium fraction (default 0.50) is supplied by the caller so no
/// fib level is hard-coded here.
/// </summary>
public sealed class DealingRange
{
    public DealingRange(Price low, Price high, DateTimeOffset anchoredAtUtc)
    {
        Guard.Against(high.Value < low.Value, "DealingRange high must be >= low.");
        Guard.Against(anchoredAtUtc.Offset != TimeSpan.Zero, "DealingRange.AnchoredAtUtc must be UTC.");
        Low = low;
        High = high;
        AnchoredAtUtc = anchoredAtUtc;
    }

    public Price Low { get; private set; }

    public Price High { get; private set; }

    public DateTimeOffset AnchoredAtUtc { get; private set; }

    public decimal Size => High.Value - Low.Value;

    /// <summary>The equilibrium price at the given fraction of the range (default 0.50).</summary>
    public decimal Equilibrium(decimal equilibriumFib) => Low.Value + (Size * equilibriumFib);

    /// <summary>Where a price sits in the range as a percent (0 = low, 100 = high); 50 for a degenerate range.</summary>
    public decimal PositionPercent(Price price)
        => Size <= 0m ? 50m : (price.Value - Low.Value) / Size * 100m;

    public bool IsDiscount(Price price, decimal equilibriumFib) => price.Value < Equilibrium(equilibriumFib);

    public bool IsPremium(Price price, decimal equilibriumFib) => price.Value > Equilibrium(equilibriumFib);

    /// <summary>Invalidation on expansion: re-anchor to a new broken-swing range.</summary>
    public void Reanchor(Price low, Price high, DateTimeOffset atUtc)
    {
        Guard.Against(high.Value < low.Value, "DealingRange high must be >= low.");
        Guard.Against(atUtc.Offset != TimeSpan.Zero, "DealingRange re-anchor time must be UTC.");
        Low = low;
        High = high;
        AnchoredAtUtc = atUtc;
    }
}
