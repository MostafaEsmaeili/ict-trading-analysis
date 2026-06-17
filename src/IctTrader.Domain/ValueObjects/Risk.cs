using System.Globalization;
using IctTrader.Domain.Common;

namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// Risk per trade as a percent of equity (plan §2.4/§5.1). Bounded to a sane (0, 100]; the ICT
/// loss-ladder and configured caps are applied by the risk-manager domain service, not here.
/// </summary>
public readonly record struct RiskPercent
{
    public RiskPercent(decimal value)
    {
        Guard.Against(value <= 0m || value > 100m, $"Risk percent must be within (0, 100] but was {value}.");
        Value = value;
    }

    public decimal Value { get; }

    public override string ToString() => $"{Value.ToString(CultureInfo.InvariantCulture)}%";
}

/// <summary>A reward-to-risk multiple, e.g. 2.5R (plan §2.5 — the configurable minimum-RR floor).</summary>
public readonly record struct RewardRatio
{
    public RewardRatio(decimal value)
    {
        Guard.Against(value <= 0m, $"Reward ratio must be positive but was {value}.");
        Value = value;
    }

    public decimal Value { get; }

    public override string ToString() => $"{Value.ToString(CultureInfo.InvariantCulture)}R";
}
