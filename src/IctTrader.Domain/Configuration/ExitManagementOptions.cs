namespace IctTrader.Domain.Configuration;

/// <summary>
/// The §2.5.9 trade-management policy (bound from <c>Ict:Execution:Management</c>) — no magic numbers. It binds the
/// T1 partial-scale-out fraction and the no-overnight day boundary the time-exit enforces. The per-style max-hold
/// cap and overnight policy live with the styles (<c>Ict:TradeStyles</c> → <see cref="StyleSettings.MaxHoldMinutes"/>
/// / <see cref="StyleSettings.AllowOvernight"/>); the stop-trail ladder knobs (50%→25% / 75%→breakeven /
/// break-even-at-1R) live under <c>Ict:Execution:Management:Trail</c>.
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

    /// <summary>
    /// The clock boundary a no-overnight style may not be held across (§2.5.1 step 9). Defaults to the ICT
    /// financial-day start <see cref="NoOvernightBoundary.NyMidnight"/> (00:00 NY, §2.1). The 17:00 ET FX-close
    /// boundary is a forward-compat seam that is NOT wired yet — selecting it fails validation here so a
    /// mis-provisioned host never silently gets unimplemented behavior.
    /// </summary>
    public NoOvernightBoundary NoOvernightBoundary { get; init; } = NoOvernightBoundary.NyMidnight;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (PartialFraction is <= 0m or >= 1m)
        {
            errors.Add($"PartialFraction must be within (0, 1) but was {PartialFraction}.");
        }

        if (NoOvernightBoundary != NoOvernightBoundary.NyMidnight)
        {
            errors.Add(
                $"NoOvernightBoundary '{NoOvernightBoundary}' is not yet supported; only NyMidnight is wired.");
        }

        return errors;
    }
}
