using System.Globalization;
using IctTrader.Domain.Common;

namespace IctTrader.Domain.ValueObjects;

/// <summary>A strictly positive market price (plan §3.0 — self-validating value object).</summary>
public readonly record struct Price
{
    public Price(decimal value)
    {
        Guard.Against(value <= 0m, $"Price must be positive but was {value}.");
        Value = value;
    }

    public decimal Value { get; }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// A non-negative distance measured in pips (plan §2 — e.g. stop distance, sweep size). Conversion to
/// price units depends on the instrument's pip size and is applied by the domain where needed.
/// </summary>
public readonly record struct Pips
{
    public Pips(decimal value)
    {
        Guard.Against(value < 0m, $"Pips cannot be negative but was {value}.");
        Value = value;
    }

    public decimal Value { get; }

    public override string ToString() => $"{Value.ToString(CultureInfo.InvariantCulture)} pips";
}
