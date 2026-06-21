namespace IctTrader.Domain.Configuration;

/// <summary>
/// The §2.5.9 stop-trail ladder policy (bound from <c>Ict:Execution:Management:Trail</c>, nested under the same
/// management subtree as <c>ExitManagementOptions</c>) — no magic numbers. The price-only ladder is the §2.5.5-row-9
/// mechanical rule (50% of the entry→T1 range → a residual-risk stop, 75% → breakeven) plus the §2.5.10
/// break-even-at-1R trigger. NOTE the §2.5.1-step-8 caveat — "No premature breakeven — require time AND price
/// (structure broken) to trail": <see cref="RequireStructureConfirmForTrail"/> reserves that overlay (default off,
/// applied by the deferred orchestrator), so the mechanical ladder here is not mistaken for the complete §2.5 rule.
/// </summary>
public sealed class StopTrailOptions
{
    public const string SectionName = "Ict:Execution:Management:Trail";

    /// <summary>Fraction of the entry→T1 range at which the stop first tightens (§2.5.5 — primary, transcript-grounded).</summary>
    public decimal TrailHalfwayFraction { get; init; } = 0.50m;

    /// <summary>The residual risk left after the first tighten — the stop sits this fraction of the original 1R from
    /// entry, still on the loss side (§2.5.5 "stop to 25% risk").</summary>
    public decimal TrailHalfwayResidualRiskFraction { get; init; } = 0.25m;

    /// <summary>Fraction of the entry→T1 range at which the stop moves to breakeven (§2.5.5 — primary).</summary>
    public decimal TrailBreakevenFraction { get; init; } = 0.75m;

    /// <summary>The R reached (favorable excursion / the original 1R) at which the stop moves to breakeven (§2.5.10
    /// ADDITION — web cross-check, configurable, NOT Mentorship-verbatim; provenance-flagged).</summary>
    public decimal BreakEvenAtR { get; init; } = 1.0m;

    /// <summary>Reserved §2.5.1-step-8 seam: when true the deferred orchestrator must ALSO confirm a structure break
    /// before trailing ("require time AND price"). The pure price-only ladder ignores it (default false).</summary>
    public bool RequireStructureConfirmForTrail { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (TrailHalfwayFraction is <= 0m or > 1m)
        {
            errors.Add($"TrailHalfwayFraction must be within (0, 1] but was {TrailHalfwayFraction}.");
        }

        if (TrailHalfwayResidualRiskFraction is <= 0m or >= 1m)
        {
            errors.Add(
                $"TrailHalfwayResidualRiskFraction must be within (0, 1) but was {TrailHalfwayResidualRiskFraction}.");
        }

        if (TrailBreakevenFraction is <= 0m or > 1m)
        {
            errors.Add($"TrailBreakevenFraction must be within (0, 1] but was {TrailBreakevenFraction}.");
        }

        if (TrailHalfwayFraction >= TrailBreakevenFraction)
        {
            errors.Add(
                $"TrailHalfwayFraction {TrailHalfwayFraction} must be below TrailBreakevenFraction {TrailBreakevenFraction}.");
        }

        if (BreakEvenAtR <= 0m)
        {
            errors.Add($"BreakEvenAtR must be positive but was {BreakEvenAtR}.");
        }

        return errors;
    }
}
