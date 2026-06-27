namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable gate for the OPTIONAL <c>CleanPriceAction</c> confluence (plan §2.5.6/§2.5.8). It operationalises the ICT
/// high-resistance-vs-low-resistance ("clean") liquidity-run concept deterministically: a leg is CLEAN when its net
/// body-to-range ratio (Σ|body| / Σrange over the displacement-leg candles) is at or above
/// <see cref="CleanBodyRatio"/>. The threshold value is INVENTED (provenance-flagged — the transcripts describe clean
/// vs HRLR qualitatively, not as a precise ratio), so it is operator-tunable. A scoring-only confluence (default ON),
/// never a hard gate. Bound from <c>Ict:Detection:CleanPriceAction</c>.
/// </summary>
public sealed class CleanPriceActionOptions
{
    public const string SectionName = "Ict:Detection:CleanPriceAction";

    /// <summary>Whether the clean-price-action confluence is scored. Default ON — additive scoring only.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The minimum body-to-range ratio (Σ|body| / Σrange over the leg) for the displacement to count as "clean".
    /// INVENTED (provenance-flagged). In (0, 1]; 1.0 = an all-body, wickless leg.
    /// </summary>
    public decimal CleanBodyRatio { get; init; } = 0.6m;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (CleanBodyRatio is <= 0m or > 1m)
        {
            errors.Add($"CleanBodyRatio must be within (0, 1] but was {CleanBodyRatio}.");
        }

        return errors;
    }
}
