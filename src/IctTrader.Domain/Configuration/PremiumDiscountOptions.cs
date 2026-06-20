using IctTrader.Domain.MarketStructure;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable premium/discount gate + dealing-range context (plan §2.5.1 step 1/6, §2.5.10). The gate is the hard
/// veto — never sell in discount, never buy in premium — measured against the dealing-range equilibrium; the
/// quadrant band is §2.5.10 provenance-flagged and NON-gating (informational only). Shared by the gate and the
/// dealing-range context detector. Bound from <c>Ict:Detection:PremiumDiscount</c>.
/// </summary>
public sealed class PremiumDiscountOptions
{
    public const string SectionName = "Ict:Detection:PremiumDiscount";

    public decimal EquilibriumPercent { get; init; } = 0.50m;

    /// <summary>Whether a price sitting exactly on the equilibrium is allowed for both directions (50% inclusive).</summary>
    public bool InclusiveAtEquilibrium { get; init; } = true;

    /// <summary>The 25%/75% quadrant band (provenance-flagged, NON-gating) — context only, never a veto.</summary>
    public decimal QuadrantBandPercent { get; init; } = 0.25m;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        // The premium/discount veto boundary is the ICT 50% equilibrium — a semantic invariant. Pin it so a
        // config change cannot silently move the "never sell in discount / never buy in premium" line.
        if (EquilibriumPercent != EquilibriumBoundaryPolicy.IctEquilibriumPercent)
        {
            errors.Add($"EquilibriumPercent must be the ICT equilibrium {EquilibriumBoundaryPolicy.IctEquilibriumPercent} but was {EquilibriumPercent}.");
        }

        if (QuadrantBandPercent is < 0m or > 0.5m)
        {
            errors.Add($"QuadrantBandPercent must be within [0, 0.5] but was {QuadrantBandPercent}.");
        }

        return errors;
    }
}
