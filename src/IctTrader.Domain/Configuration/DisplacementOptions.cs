namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable displacement energy gate (plan §2.5.7 caveat 5 — "quantify displacement"). The transcript-faithful
/// core is body-to-range AND body-vs-ATR; <see cref="MinDisplacementPips"/> is DEMOTED to a disclosed,
/// non-transcript addition defaulting OFF (0) so it is never a silent third hard filter (§2.5.5 "no fixed
/// pip"). Bound from <c>Ict:Displacement</c>.
/// </summary>
public sealed class DisplacementOptions
{
    public const string SectionName = "Ict:Displacement";

    public int AtrPeriod { get; init; } = 14;

    public decimal AtrMultiple { get; init; } = 1.5m;

    public decimal MinBodyToRangeRatio { get; init; } = 0.50m;

    /// <summary>OFF by default (0). An operator-tunable absolute floor, NOT a transcript rule (verifier-demoted).</summary>
    public decimal MinDisplacementPips { get; init; } = 0m;

    public int DisplacementLegMaxBars { get; init; } = 3;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (AtrPeriod < 1)
        {
            errors.Add($"AtrPeriod must be at least 1 but was {AtrPeriod}.");
        }

        if (AtrMultiple < 0m)
        {
            errors.Add($"AtrMultiple cannot be negative but was {AtrMultiple}.");
        }

        if (MinBodyToRangeRatio is < 0m or > 1m)
        {
            errors.Add($"MinBodyToRangeRatio must be within [0, 1] but was {MinBodyToRangeRatio}.");
        }

        return errors;
    }
}
