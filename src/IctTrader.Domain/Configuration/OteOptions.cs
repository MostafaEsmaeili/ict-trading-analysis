namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable Optimal-Trade-Entry fib band (plan §2.4/§2.5.1 step 7, §2.5.10). The canonical band is 62–79% of the
/// displacement leg with a 70.5% sweet spot (Primer-sourced/provenance-flagged); the Ep41 variant narrows the
/// upper edge to 70%. The OTE is anchored on the pre-validated displacement leg the detector consumes — it does
/// NOT re-quantify displacement; body-vs-wick anchoring follows the leg's construction (spec §5 open item 4).
/// Bound from <c>Ict:Detection:Ote</c>.
/// </summary>
public sealed class OteOptions
{
    public const string SectionName = "Ict:Detection:Ote";

    public decimal LowerFib { get; init; } = 0.62m;

    public decimal UpperFib { get; init; } = 0.79m;

    /// <summary>The 70.5% sweet spot — <c>isPrimerSourcedDefault</c>; must lie within the effective band.</summary>
    public decimal SweetSpotFib { get; init; } = 0.705m;

    /// <summary>Ep41 variant: narrow the band upper edge to 70% (default OFF — keep the 62–79% canon).</summary>
    public bool UseEp41Variant { get; init; }

    public decimal Ep41UpperFib { get; init; } = 0.70m;

    public decimal EffectiveUpperFib => UseEp41Variant ? Ep41UpperFib : UpperFib;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (LowerFib is < 0m or > 1m)
        {
            errors.Add($"LowerFib must be within [0, 1] but was {LowerFib}.");
        }

        if (EffectiveUpperFib is < 0m or > 1m)
        {
            errors.Add($"The effective upper fib must be within [0, 1] but was {EffectiveUpperFib}.");
        }

        if (LowerFib >= EffectiveUpperFib)
        {
            errors.Add($"LowerFib ({LowerFib}) must be below the effective upper fib ({EffectiveUpperFib}).");
        }

        if (SweetSpotFib < LowerFib || SweetSpotFib > EffectiveUpperFib)
        {
            errors.Add($"SweetSpotFib {SweetSpotFib} must lie within the band [{LowerFib}, {EffectiveUpperFib}].");
        }

        return errors;
    }
}
