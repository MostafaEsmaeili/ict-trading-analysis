namespace IctTrader.Domain.Configuration;

/// <summary>
/// The §2.5.9 trade-management policy (bound from <c>Ict:Execution:Management</c>) — no magic numbers. This slice
/// binds ONLY the partial-scale-out fraction; the stop-trail (50%→25% / 75%→breakeven / break-even-at-1R) and the
/// time-exit (max-hold / no-overnight) knobs are NOT introduced yet — they land with their own follow-on slices
/// so <c>ValidateOnStart</c> stays honest about what is actually wired.
/// </summary>
public sealed class ExitManagementOptions
{
    public const string SectionName = "Ict:Execution:Management";

    /// <summary>
    /// The fraction of the position scaled out at the T1 partial. INVENTED engineering default — the transcripts
    /// say take a partial at T1 (§2.5.1 step 9) but never state the size; 0.50 is the common ICT "scale half"
    /// practice and is operator-tunable. Must be strictly inside (0, 1): 0 is no scale, 1 is a full close.
    /// </summary>
    public decimal PartialFraction { get; init; } = 0.50m;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (PartialFraction is <= 0m or >= 1m)
        {
            errors.Add($"PartialFraction must be within (0, 1) but was {PartialFraction}.");
        }

        return errors;
    }
}
