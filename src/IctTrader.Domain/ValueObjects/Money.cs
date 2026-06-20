using System.Globalization;

namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// A signed amount of account currency (plan §5.1/§7) — equity, realized P&amp;L, a budgeted risk amount.
/// It is deliberately sign-agnostic: a loss is a negative <see cref="Money"/>, equity is held positive by the
/// <c>PaperAccount</c> aggregate (not here), and a budgeted risk is held positive by the sizer. Rounding to a
/// currency precision is a persistence/display concern, so the value object keeps the raw decimal.
/// </summary>
public readonly record struct Money
{
    public static readonly Money Zero = new(0m);

    public Money(decimal amount) => Amount = amount;

    public decimal Amount { get; }

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);

    public static Money operator *(Money money, decimal multiplier) => new(money.Amount * multiplier);

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    public override string ToString() => Amount.ToString("0.00", CultureInfo.InvariantCulture);
}
