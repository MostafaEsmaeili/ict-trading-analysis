namespace IctTrader.Domain.Configuration;

/// <summary>
/// The paper-trade risk policy (plan §5.1, bound from <c>Ict:Risk</c>) — no magic numbers. This slice uses a
/// flat <see cref="BaseRiskPercent"/> per trade plus the aggregate portfolio cap; the adaptive loss-ladder /
/// win-cycle (§2.4/§2.5.5) and the per-trade hard-max are a deferred fast-follow that will extend this POCO.
/// </summary>
public sealed class RiskOptions
{
    public const string SectionName = "Ict:Risk";

    /// <summary>The risk taken per trade as a percent of equity (§2.5.5 default 1% with-bias).</summary>
    public decimal BaseRiskPercent { get; init; } = 1.0m;

    /// <summary>The aggregate open-risk cap across all open trades (§2.5.10 ≈5%).</summary>
    public decimal MaxOpenPortfolioRiskPercent { get; init; } = 5.0m;

    /// <summary>The minimum stop distance a sized trade may carry (FX ~10 pips, §2.2/§2.5.5).</summary>
    public decimal MinStopDistancePips { get; init; } = 10m;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (BaseRiskPercent is <= 0m or > 100m)
        {
            errors.Add($"BaseRiskPercent must be within (0, 100] but was {BaseRiskPercent}.");
        }

        if (MaxOpenPortfolioRiskPercent is <= 0m or > 100m)
        {
            errors.Add($"MaxOpenPortfolioRiskPercent must be within (0, 100] but was {MaxOpenPortfolioRiskPercent}.");
        }

        if (BaseRiskPercent > MaxOpenPortfolioRiskPercent)
        {
            errors.Add(
                $"BaseRiskPercent {BaseRiskPercent} cannot exceed the portfolio cap {MaxOpenPortfolioRiskPercent}.");
        }

        if (MinStopDistancePips <= 0m)
        {
            errors.Add($"MinStopDistancePips must be positive but was {MinStopDistancePips}.");
        }

        return errors;
    }
}
