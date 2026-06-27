using IctTrader.Domain.Instruments;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// The §2.5.1-step-7 entry-arming policy (bound from <c>Ict:Execution:Entry</c>) — no magic numbers. It selects the
/// entry <see cref="Mode"/> (the module orchestrator branches on it) and the no-chase max-wait backstop. The active
/// killzone hunt-set the no-chase killzone-end rung tests against is reused from <c>Ict:Scanning</c>
/// (<see cref="KillzoneEntryOptions.ResolvedActiveKillzones"/>) so the arm window and the entry window cannot drift apart.
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

    /// <summary>
    /// EG-3 v1 (Ep10/29/07/22/35): record the resting-limit fill at the touched price clamped to a small entry-anchored
    /// band, rather than exactly at the plan entry. Default OFF (the byte-identical limit-level fill). This is a
    /// DIAGNOSTIC nicety only — <see cref="Trading.PaperTradeFactory.OpenArmed"/> still opens the trade at the plan
    /// entry, so the frozen-1R invariant is preserved (the open price never moves). The "open at the touched price"
    /// real-economics variant is deferred (it would break reserve == RiskBudget).
    /// </summary>
    public bool UseCloseProximityEntry { get; init; }

    /// <summary>
    /// The half-width (in pips) of the EG-3 close-proximity fill band around the entry. INVENTED — the transcripts say
    /// enter "close to" the level but give no tolerance, so this is an operator-tunable, provenance-flagged guard, kept
    /// small. Read only when <see cref="UseCloseProximityEntry"/> is on; must be non-negative.
    /// </summary>
    public decimal CloseProximityTolerancePips { get; init; } = 2m;

    /// <summary>
    /// Returns a copy with the instrument-class scalar overrides applied where present
    /// (<see cref="CloseProximityTolerancePips"/>, only live under <see cref="UseCloseProximityEntry"/>). A
    /// <see cref="InstrumentOptionOverrides.None"/> / FX bundle leaves every field unchanged (byte-identical). The
    /// entry <see cref="Mode"/> and the no-chase <see cref="MaxWaitMinutes"/> backstop are instrument-agnostic.
    /// </summary>
    public EntryManagementOptions WithInstrumentOverrides(InstrumentOptionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return new EntryManagementOptions
        {
            Mode = Mode,
            MaxWaitMinutes = MaxWaitMinutes,
            UseCloseProximityEntry = UseCloseProximityEntry,
            CloseProximityTolerancePips = overrides.CloseProximityTolerancePips ?? CloseProximityTolerancePips,
        };
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(Mode))
        {
            errors.Add($"Mode must be a defined {nameof(EntryMode)} value but was {(int)Mode}.");
        }

        if (MaxWaitMinutes <= 0)
        {
            errors.Add($"MaxWaitMinutes must be positive but was {MaxWaitMinutes}.");
        }

        if (CloseProximityTolerancePips < 0m)
        {
            errors.Add($"CloseProximityTolerancePips cannot be negative but was {CloseProximityTolerancePips}.");
        }

        return errors;
    }
}
