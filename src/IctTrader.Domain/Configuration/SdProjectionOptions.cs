namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable standard-deviation projection targets (decision TGR-1/2; plan §2.5.10 #5). One SD unit = the displacement-leg
/// length (body-to-body, EG-1), projected beyond the terminus as the −1 / −1.5 / −2 SD draw. The construct is
/// Mentorship-grounded but shipped <see cref="Enabled"/>=false so the §2.5 default ladder stays byte-identical until a
/// consumer opts in. The negative-fib variant (−0.27/−0.62/−1.0) is a Primer-sourced opt-in, NOT the default. Bound
/// from <c>Ict:Detection:SdProjection</c>.
/// </summary>
public sealed class SdProjectionOptions
{
    public const string SectionName = "Ict:Detection:SdProjection";

    /// <summary>Off by default — the SD tiers are computed/tested but not merged into the priced ladder until opted in.</summary>
    public bool Enabled { get; init; }

    /// <summary>The SD multiples (the −1 / −1.5 / −2 SD set) — strictly ascending, positive.</summary>
    public IReadOnlyList<decimal> Multiples { get; init; } = [1.0m, 1.5m, 2.0m];

    /// <summary>When merging into the ladder, keep SD tiers that fall INSIDE the range draw (default: drop them, so the
    /// ladder stays a clean monotone {T1, T2_range, deeper SD tiers}). Consumed by the deferred ladder-merge.</summary>
    public bool IncludeTiersInsideRangeDraw { get; init; }

    /// <summary>The Primer-flagged negative-fib variant — OFF by default; when on it REPLACES the SD multiples.</summary>
    public NegativeFibOptions NegativeFibVariant { get; init; } = new();

    /// <summary>The multiples actually projected: the negative-fib coefficients when that variant is on, else the SD set.</summary>
    public IReadOnlyList<decimal> EffectiveMultiples =>
        NegativeFibVariant.Enabled ? NegativeFibVariant.Coefficients : Multiples;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        ValidateAscendingPositive(Multiples, nameof(Multiples), max: null, errors);

        if (NegativeFibVariant.Enabled)
        {
            ValidateAscendingPositive(NegativeFibVariant.Coefficients, nameof(NegativeFibVariant), max: 1m, errors);
        }

        return errors;
    }

    private static void ValidateAscendingPositive(
        IReadOnlyList<decimal> values, string name, decimal? max, List<string> errors)
    {
        if (values is null || values.Count == 0)
        {
            errors.Add($"{name} must contain at least one value.");
            return;
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0m || (max is { } cap && values[i] > cap))
            {
                var range = max is { } c ? $"(0, {c}]" : "> 0";
                errors.Add($"{name}[{i}] must be {range} but was {values[i]}.");
            }

            if (i > 0 && values[i] <= values[i - 1])
            {
                errors.Add($"{name} must be strictly ascending but {values[i]} <= {values[i - 1]} at index {i}.");
            }
        }
    }
}

/// <summary>The Primer-sourced negative-fib target variant (−0.27/−0.62/−1.0) — provenance-flagged, OFF by default.</summary>
public sealed class NegativeFibOptions
{
    public bool Enabled { get; init; }

    /// <summary>The negative-fib coefficients — when <see cref="Enabled"/>, projected on the same terminus axis.</summary>
    public IReadOnlyList<decimal> Coefficients { get; init; } = [0.27m, 0.62m, 1.0m];
}
