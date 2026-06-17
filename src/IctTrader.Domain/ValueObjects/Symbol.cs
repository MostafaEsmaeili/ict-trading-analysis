using IctTrader.Domain.Common;

namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// A normalized instrument symbol, e.g. "EURUSD" (plan §6.1 — provider symbols normalize to this so the
/// rest of the domain is provider-agnostic).
/// </summary>
public sealed record Symbol
{
    public Symbol(string value)
    {
        Guard.AgainstNullOrWhiteSpace(value, "Symbol must not be empty.");
        Value = value.Trim().ToUpperInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
