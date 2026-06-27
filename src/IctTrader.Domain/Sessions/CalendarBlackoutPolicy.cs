using IctTrader.Domain.Configuration;

namespace IctTrader.Domain.Sessions;

/// <summary>
/// The single, shared definition of the §2.5.2 calendar HARD-gate blackout (plan §2.5.2/§2.5.8): whether a given
/// New-York date is inside the post-FOMC window or the NFP-week block. Both the required <c>CalendarGateDetector</c>
/// (which WITHHOLDS <c>CalendarClear</c> on a blacked-out day) and the optional <c>CalendarDriverDetector</c> (which
/// withholds the low-weight <c>CalendarDriver</c> confluence on a blacked-out day, so the release minute is not
/// double-counted, decision TGR-3) consume this ONE policy — so the two can never disagree about which days are blocked.
/// Pure: date arithmetic only, host-zone-independent.
/// </summary>
public static class CalendarBlackoutPolicy
{
    /// <summary>Whether <paramref name="date"/> is inside the post-FOMC or NFP-week blackout per the gate options.</summary>
    public static bool IsBlackedOut(DateOnly date, IReadOnlyList<EconomicEvent> events, CalendarOptions options)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);

        foreach (var economicEvent in events)
        {
            switch (economicEvent.Type)
            {
                case CalendarEventType.Fomc when options.BlockPostFomc:
                    // The FOMC day (knee-jerk) and the configured days after it (post-FOMC).
                    if (date >= economicEvent.NyDate
                        && date.DayNumber - economicEvent.NyDate.DayNumber <= options.FomcBlockDaysAfter)
                    {
                        return true;
                    }

                    break;

                case CalendarEventType.Nfp when options.BlockNfpWeek:
                    // The configured days up to and including the NFP release (Wed/Thu/Fri for a Friday NFP).
                    if (date <= economicEvent.NyDate
                        && economicEvent.NyDate.DayNumber - date.DayNumber <= options.NfpBlockDaysBefore)
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }
}
