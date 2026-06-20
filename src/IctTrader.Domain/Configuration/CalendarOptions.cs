namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable economic-calendar no-trade gate (plan §2.5.2/§2.5.8 — foundation verdict fix 1). The §2.5.2 ALL-must-
/// be-true clause blocks post-FOMC days and the NFP week from Wednesday (covering NFP Thursday + Friday). This is
/// the required HARD gate, distinct from the low-weight <c>CalendarDriver</c> score contributor. When the calendar
/// has not been loaded the behaviour is config-gated (fail-open by default — never block on missing data). Bound
/// from <c>Ict:Detection:Calendar</c>.
/// </summary>
public sealed class CalendarOptions
{
    public const string SectionName = "Ict:Detection:Calendar";

    public bool BlockPostFomc { get; init; } = true;

    /// <summary>Block the FOMC announcement day and this many days after it (1 ⇒ knee-jerk day + post-FOMC).</summary>
    public int FomcBlockDaysAfter { get; init; } = 1;

    public bool BlockNfpWeek { get; init; } = true;

    /// <summary>Block this many days BEFORE the NFP release (2 ⇒ Wednesday/Thursday/Friday of NFP week).</summary>
    public int NfpBlockDaysBefore { get; init; } = 2;

    /// <summary>When no calendar has been loaded, whether to block (true) or treat as clear (false, fail-open).</summary>
    public bool BlockWhenCalendarUnavailable { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (FomcBlockDaysAfter < 0)
        {
            errors.Add($"FomcBlockDaysAfter cannot be negative but was {FomcBlockDaysAfter}.");
        }

        if (NfpBlockDaysBefore < 0)
        {
            errors.Add($"NfpBlockDaysBefore cannot be negative but was {NfpBlockDaysBefore}.");
        }

        return errors;
    }
}
