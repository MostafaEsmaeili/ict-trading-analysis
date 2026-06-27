using System.Globalization;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// The economic-calendar no-trade gate (plan §2.5.2/§2.5.8). Emits <see cref="ConfluenceCondition.CalendarClear"/>
/// (required) ONLY when the current New-York date is not calendar-blocked: a post-FOMC window or the days up to
/// and including the NFP release (Wed→Fri for a Friday NFP). It reads the events loaded onto the <see cref="MarketContext"/>; a
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

        if (CalendarBlackoutPolicy.IsBlackedOut(date, context.EconomicEvents, _options))
        {
            return DetectorResult.NoMatch; // blocked day -> CalendarClear absent -> setup rejected
        }

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.CalendarDate] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        return DetectorResult.Match(null, null, ReasonFragments.CalendarClear(date), evidence);
    }
}
