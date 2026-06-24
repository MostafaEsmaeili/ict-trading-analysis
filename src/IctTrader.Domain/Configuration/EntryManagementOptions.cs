namespace IctTrader.Domain.Configuration;

/// <summary>
/// The §2.5.1-step-7 entry-arming policy (bound from <c>Ict:Execution:Entry</c>) — no magic numbers. It selects the
/// entry <see cref="Mode"/> (the module orchestrator branches on it) and the no-chase max-wait backstop. The active
/// killzone hunt-set the no-chase killzone-end rung tests against is reused from <c>Ict:Detection:Killzone</c>
/// (<see cref="KillzoneEntryOptions.ActiveKillzones"/>) so the arm window and the entry window cannot drift apart.
/// </summary>
public sealed class EntryManagementOptions
{
    public const string SectionName = "Ict:Execution:Entry";

    /// <summary>How a confirmed setup becomes a trade — defaults to the faithful <see cref="EntryMode.Armed"/>.</summary>
    public EntryMode Mode { get; init; } = EntryMode.Armed;

    /// <summary>
    /// The maximum minutes a resting limit may wait unfilled before the no-chase backstop cancels it. INVENTED — the
    /// transcripts give a max-HOLD for an OPEN trade (§2.5.1 step 9) but no max-WAIT for a pending limit, so this is an
    /// operator-tunable guard, kept GENEROUS so the killzone-end rung (the §2.5.1-step-3 entry window) normally fires
    /// first. Provenance-flagged; must be positive.
    /// </summary>
    public int MaxWaitMinutes { get; init; } = 240;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MaxWaitMinutes <= 0)
        {
            errors.Add($"MaxWaitMinutes must be positive but was {MaxWaitMinutes}.");
        }

        return errors;
    }
}
