using IctTrader.Domain.Instruments;

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

    /// <summary>The §2.5.5 methodology ceiling on per-trade risk — an operator may configure a lower hard max but
    /// never one above this (mirrors the hard 2:1 reward floor pattern). Not a magic number: the §2.5.5 "hard max 4.5%".</summary>
    private const decimal AbsoluteHardMaxRiskPercent = 4.5m;

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
    /// <para>Defaults to EMPTY so the .NET config binder REPLACES rather than APPENDS to a pre-populated initializer
    /// (see MarketContextOptions.cs for the documented rationale) — a non-empty default would silently prepend
    /// <c>[0.5, 0.25]</c> to an operator's ladder. Consume <see cref="ResolvedLossLadderPercents"/>, never this.</para>
    /// </summary>
    public IReadOnlyList<decimal> LossLadderPercents { get; init; } = [];

    private static readonly IReadOnlyList<decimal> DefaultLossLadderPercents = [0.5m, 0.25m];

    /// <summary>The ladder to consume — the configured rungs, or the §2.5.5 default when none is set.</summary>
    public IReadOnlyList<decimal> ResolvedLossLadderPercents =>
        LossLadderPercents.Count == 0 ? DefaultLossLadderPercents : LossLadderPercents;

    /// <summary>Consecutive wins after which risk drops to the lowest unit to protect a run's profits (§2.4 default 5).</summary>
    public int ConsecutiveWinsForLowestUnit { get; init; } = 5;

    /// <summary>The fraction of a drawdown (peak→trough) that equity must recover before risk restores to base (§2.5.5 default 0.50).</summary>
    public decimal DipRecoveryFraction { get; init; } = 0.50m;

    /// <summary>
    /// The hard ceiling on per-trade risk (§2.5.5 "hard max 4.5%"). Mentorship-primary at 4.5%; the §5.1 "max 3%" figure
    /// is the more conservative framework default and is intentionally NOT used here (kept configurable).
    /// </summary>
    public decimal HardMaxRiskPercent { get; init; } = 4.5m;

    /// <summary>
    /// Returns a copy with the instrument-class scalar overrides applied where present (only
    /// <see cref="MinStopDistancePips"/> here). A <see cref="InstrumentOptionOverrides.None"/> / FX bundle leaves
    /// every field at its global value, so the result is field-equal to <c>this</c> (the FX path stays
    /// byte-identical). All other risk knobs (ladder, win-cycle, caps) are instrument-agnostic and unchanged.
    /// </summary>
    public RiskOptions WithInstrumentOverrides(InstrumentOptionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return new RiskOptions
        {
            BaseRiskPercent = BaseRiskPercent,
            MaxOpenPortfolioRiskPercent = MaxOpenPortfolioRiskPercent,
            MinStopDistancePips = overrides.MinStopDistancePips ?? MinStopDistancePips,
            LossLadderPercents = LossLadderPercents,
            ConsecutiveWinsForLowestUnit = ConsecutiveWinsForLowestUnit,
            DipRecoveryFraction = DipRecoveryFraction,
            HardMaxRiskPercent = HardMaxRiskPercent,
        };
    }

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

        // An empty CONFIGURED ladder is VALID — it means "use the §2.5.5 default" (applied by the resolved accessor).
        // We validate the EFFECTIVE rungs so a genuine bad override is still caught with a message that matches what
        // the operator wrote (no prepended default), while an unconfigured host stays valid.
        var ladder = ResolvedLossLadderPercents;
        for (var i = 0; i < ladder.Count; i++)
        {
            if (ladder[i] is <= 0m or > 100m)
            {
                errors.Add($"LossLadderPercents[{i}] must be within (0, 100] but was {ladder[i]}.");
            }

            if (i > 0 && ladder[i] >= ladder[i - 1])
            {
                errors.Add("LossLadderPercents must be strictly descending (each step below the previous).");
            }
        }

        if (ladder[0] >= BaseRiskPercent)
        {
            errors.Add(
                $"The first loss-ladder step {ladder[0]} must sit below BaseRiskPercent {BaseRiskPercent}.");
        }

        if (ConsecutiveWinsForLowestUnit < 1)
        {
            errors.Add($"ConsecutiveWinsForLowestUnit must be at least 1 but was {ConsecutiveWinsForLowestUnit}.");
        }

        if (DipRecoveryFraction is <= 0m or > 1m)
        {
            errors.Add($"DipRecoveryFraction must be within (0, 1] but was {DipRecoveryFraction}.");
        }

        if (HardMaxRiskPercent is <= 0m || HardMaxRiskPercent > AbsoluteHardMaxRiskPercent)
        {
            errors.Add(
                $"HardMaxRiskPercent must be within (0, {AbsoluteHardMaxRiskPercent}] (the §2.5.5 hard max) but was {HardMaxRiskPercent}.");
        }

        if (HardMaxRiskPercent < BaseRiskPercent)
        {
            errors.Add($"HardMaxRiskPercent {HardMaxRiskPercent} cannot sit below BaseRiskPercent {BaseRiskPercent}.");
        }

        return errors;
    }
}
