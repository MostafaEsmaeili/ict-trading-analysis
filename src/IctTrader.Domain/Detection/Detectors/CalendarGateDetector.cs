using System.Globalization;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// The economic-calendar no-trade gate (plan §2.5.2/§2.5.8). Emits <see cref="ConfluenceCondition.CalendarClear"/>
/// (required) ONLY when the current New-York date is not calendar-blocked: a post-FOMC window or the NFP week from
/// Wednesday (covering NFP Thursday + Friday). It reads the events loaded onto the <see cref="MarketContext"/>; a
/// blocked day emits no match, so the FSM (which requires CalendarClear) rejects the setup. When the calendar has
/// not been loaded the behaviour is config-gated (fail-open by default). All dates are New-York to stay
/// host-zone-independent (plan §4.8).
/// </summary>
public sealed class CalendarGateDetector : ISetupDetector
{
    private readonly CalendarOptions _options;

    public CalendarGateDetector(CalendarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.CalendarClear;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.CurrentNewYorkDate is not { } date || !context.IsCalendarLoaded)
        {
            // No NY date yet or no calendar loaded -> config-gated (fail-open keeps trading, fail-closed blocks).
            return _options.BlockWhenCalendarUnavailable
                ? DetectorResult.NoMatch
                : DetectorResult.Match(null, null, ReasonFragments.CalendarClearUnverified());
        }

        if (IsCalendarBlocked(context, date))
        {
            return DetectorResult.NoMatch; // blocked day -> CalendarClear absent -> setup rejected
        }

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.CalendarDate] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        return DetectorResult.Match(null, null, ReasonFragments.CalendarClear(date), evidence);
    }

    private bool IsCalendarBlocked(MarketContext context, DateOnly date)
    {
        foreach (var economicEvent in context.EconomicEvents)
        {
            switch (economicEvent.Type)
            {
                case CalendarEventType.Fomc when _options.BlockPostFomc:
                    // The FOMC day (knee-jerk) and the configured days after it (post-FOMC).
                    if (date >= economicEvent.NyDate
                        && date.DayNumber - economicEvent.NyDate.DayNumber <= _options.FomcBlockDaysAfter)
                    {
                        return true;
                    }

                    break;

                case CalendarEventType.Nfp when _options.BlockNfpWeek:
                    // The configured days BEFORE the NFP release (Wed/Thu/Fri of NFP week) through the release.
                    if (date <= economicEvent.NyDate
                        && economicEvent.NyDate.DayNumber - date.DayNumber <= _options.NfpBlockDaysBefore)
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }
}
