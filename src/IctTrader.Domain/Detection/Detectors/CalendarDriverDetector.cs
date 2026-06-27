using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Emits the OPTIONAL <see cref="ConfluenceCondition.CalendarDriver"/> confluence (0.35, decision TGR-3,
/// plan §2.5.2/§2.5.8). DISTINCT from the required hard-gate <see cref="CalendarGateDetector"/>: a same-NY-day
/// high-impact economic DRIVER gives the session a reason to be delivered toward a draw, so it is low-weight scoring
/// confluence — but ONLY when that day is NOT inside the hard-gate blackout the gate already vetoes (so the release
/// minute is owned by the required <see cref="ConfluenceCondition.CalendarClear"/>, never double-counted). It consumes
/// the SAME <see cref="CalendarBlackoutPolicy"/> as the gate, so the two can never disagree about which days are blocked.
///
/// <para>Non-directional (a driver does not set a side, TGR-3). A confluence (scoring-only), NOT a RequiredCondition —
/// its absence never blocks a setup. Fail-OPEN (no match, not an error) when the calendar is unloaded, mirroring the
/// gate's fail-open default. All dates are New-York to stay host-zone-independent (plan §4.8).</para>
/// </summary>
public sealed class CalendarDriverDetector : ISetupDetector
{
    private readonly CalendarDriverOptions _options;
    private readonly CalendarOptions _gateOptions;

    public CalendarDriverDetector(CalendarDriverOptions options, CalendarOptions gateOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(gateOptions);
        _options = options;
        _gateOptions = gateOptions;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.CalendarDriver;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_options.Enabled || context.CurrentNewYorkDate is not { } date || !context.IsCalendarLoaded)
        {
            return DetectorResult.NoMatch; // disabled, no NY date yet, or fail-open on an unloaded calendar
        }

        // A driver day already inside the hard-gate blackout is owned by the required CalendarClear (the release minute
        // is blocked separately); withhold the driver confluence so the same event is never double-counted (TGR-3).
        if (CalendarBlackoutPolicy.IsBlackedOut(date, context.EconomicEvents, _gateOptions))
        {
            return DetectorResult.NoMatch;
        }

        var drivers = _options.ResolvedDriverEventTypes;
        foreach (var economicEvent in context.EconomicEvents)
        {
            if (economicEvent.NyDate == date && drivers.Contains(economicEvent.Type))
            {
                var evidence = new Dictionary<string, object>
                {
                    [EvidenceKeys.CalendarDate] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    [EvidenceKeys.DriverEventType] = economicEvent.Type.ToString(),
                };

                return DetectorResult.Match(
                    direction: null, keyLevel: null, ReasonFragments.CalendarDriver(economicEvent.Type, date), evidence);
            }
        }

        return DetectorResult.NoMatch; // no same-day driver event
    }
}
