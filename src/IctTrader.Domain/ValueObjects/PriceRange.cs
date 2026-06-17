using IctTrader.Domain.Common;

namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// An inclusive price range with a 50% equilibrium — the premium/discount boundary of a dealing range
/// (plan §2.4/§2.5: discount &lt; 50% is bullish territory, premium &gt; 50% is bearish).
/// </summary>
public readonly record struct PriceRange
{
    public PriceRange(Price low, Price high)
    {
        Guard.Against(high.Value < low.Value, $"PriceRange high {high} must be >= low {low}.");
        Low = low;
        High = high;
    }

    public Price Low { get; }

    public Price High { get; }

    public decimal Size => High.Value - Low.Value;

    /// <summary>50% of the range — the equilibrium that splits premium from discount (plan §2.4).</summary>
    public decimal Equilibrium => Low.Value + (Size / 2m);

    public bool Contains(Price price) => price.Value >= Low.Value && price.Value <= High.Value;

    public bool IsDiscount(Price price) => price.Value < Equilibrium;

    public bool IsPremium(Price price) => price.Value > Equilibrium;
}
