namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable target-ladder construction (plan §2.5.1 step 9). The first slice models exactly two tiers: T1 (the
/// partial) at the equilibrium of the entry→T2 leg, and T2 (the runner) = the draw-on-liquidity target. The
/// equilibrium fraction is the transcript 50% (§2.5.5), surfaced here so it is not a literal; the SD −1/−1.5/−2
/// extension ladder (used only when there is no opposing range target) is deferred. Bound from
/// <c>Ict:Detection:TargetLadder</c>.
/// </summary>
public sealed class TargetLadderOptions
{
    public const string SectionName = "Ict:Detection:TargetLadder";

    /// <summary>Where T1 (the partial) sits along the entry→T2 leg — the §2.5.5 50% equilibrium.</summary>
    public decimal T1EquilibriumFraction { get; init; } = 0.5m;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (T1EquilibriumFraction is <= 0m or >= 1m)
        {
            errors.Add($"T1EquilibriumFraction must be within (0, 1) but was {T1EquilibriumFraction}.");
        }

        return errors;
    }
}
