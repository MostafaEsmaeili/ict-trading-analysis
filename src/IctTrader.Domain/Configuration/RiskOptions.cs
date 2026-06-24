namespace IctTrader.Domain.Configuration;

/// <summary>
/// The paper-trade risk policy (plan §5.1, bound from <c>Ict:Risk</c>) — no magic numbers. <see cref="BaseRiskPercent"/>
/// is the with-bias default; the adaptive <see cref="IctTrader.Domain.Trading.IRiskManager"/> reduces it down the
/// <see cref="LossLadderPercents"/> ladder during a drawdown (restoring once <see cref="DipRecoveryFraction"/> of the
/// dip is recovered) and to the lowest unit after <see cref="ConsecutiveWinsForLowestUnit"/> wins, all clamped by
/// <see cref="HardMaxRiskPercent"/> (§2.4/§2.5.5). The aggregate portfolio cap (§2.5.10) is separate.
/// </summary>
public sealed class RiskOptions
{
    public const string SectionName = "Ict:Risk";

    /// <summary>The risk taken per trade as a percent of equity with no active streak/drawdown (§2.5.5 default 1% with-bias).</summary>
    public decimal BaseRiskPercent { get; init; } = 1.0m;

    /// <summary>The aggregate open-risk cap across all open trades (§2.5.10 ≈5%).</summary>
    public decimal MaxOpenPortfolioRiskPercent { get; init; } = 5.0m;

    /// <summary>The minimum stop distance a sized trade may carry (FX ~10 pips, §2.2/§2.5.5).</summary>
    public decimal MinStopDistancePips { get; init; } = 10m;

    /// <summary>
    /// The strictly-descending per-trade risk reductions taken while a drawdown is unrecovered, indexed by consecutive
    /// losses (1 loss → element 0, ≥count → the last element). §2.5.5 ladder = base 1% then <c>[0.5, 0.25]</c>; the last
    /// element is also the "lowest unit" the win-cycle drops to. 1% → 0.5% → 0.25% is Mentorship-verbatim (Ep41 "one
    /// percent… half of one percent… a quarter of one percent"); the rungs stay configurable per broker/operator.
    /// </summary>
    public IReadOnlyList<decimal> LossLadderPercents { get; init; } = [0.5m, 0.25m];

    /// <summary>Consecutive wins after which risk drops to the lowest unit to protect a run's profits (§2.4 default 5).</summary>
    public int ConsecutiveWinsForLowestUnit { get; init; } = 5;

    /// <summary>The fraction of a drawdown (peak→trough) that equity must recover before risk restores to base (§2.5.5 default 0.50).</summary>
    public decimal DipRecoveryFraction { get; init; } = 0.50m;

    /// <summary>
    /// The hard ceiling on per-trade risk (§2.5.5 "hard max 4.5%"). Mentorship-primary at 4.5%; the §5.1 "max 3%" figure
    /// is the more conservative framework default and is intentionally NOT used here (kept configurable).
    /// </summary>
    public decimal HardMaxRiskPercent { get; init; } = 4.5m;

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

        if (LossLadderPercents is null || LossLadderPercents.Count == 0)
        {
            errors.Add("LossLadderPercents must contain at least one reduction step.");
        }
        else
        {
            for (var i = 0; i < LossLadderPercents.Count; i++)
            {
                if (LossLadderPercents[i] is <= 0m or > 100m)
                {
                    errors.Add($"LossLadderPercents[{i}] must be within (0, 100] but was {LossLadderPercents[i]}.");
                }

                if (i > 0 && LossLadderPercents[i] >= LossLadderPercents[i - 1])
                {
                    errors.Add("LossLadderPercents must be strictly descending (each step below the previous).");
                }
            }

            if (LossLadderPercents[0] >= BaseRiskPercent)
            {
                errors.Add(
                    $"The first loss-ladder step {LossLadderPercents[0]} must sit below BaseRiskPercent {BaseRiskPercent}.");
            }
        }

        if (ConsecutiveWinsForLowestUnit < 1)
        {
            errors.Add($"ConsecutiveWinsForLowestUnit must be at least 1 but was {ConsecutiveWinsForLowestUnit}.");
        }

        if (DipRecoveryFraction is <= 0m or > 1m)
        {
            errors.Add($"DipRecoveryFraction must be within (0, 1] but was {DipRecoveryFraction}.");
        }

        if (HardMaxRiskPercent is <= 0m or > 100m)
        {
            errors.Add($"HardMaxRiskPercent must be within (0, 100] but was {HardMaxRiskPercent}.");
        }

        if (HardMaxRiskPercent < BaseRiskPercent)
        {
            errors.Add($"HardMaxRiskPercent {HardMaxRiskPercent} cannot sit below BaseRiskPercent {BaseRiskPercent}.");
        }

        return errors;
    }
}
