using System.Globalization;
using IctTrader.Domain.Common;

namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// A strictly positive position size in lots (plan §5.1). A paper trade can never exist with a zero or
/// negative size — the sizer floors to the instrument's lot step and rejects anything below the minimum lot,
/// so a constructed <see cref="PositionSize"/> is always tradeable.
/// </summary>
public readonly record struct PositionSize
{
    public PositionSize(decimal lots)
    {
        Guard.Against(lots <= 0m, $"Position size must be a positive number of lots but was {lots}.");
        Lots = lots;
    }

    public decimal Lots { get; }

    public override string ToString() => $"{Lots.ToString(CultureInfo.InvariantCulture)} lots";
}
