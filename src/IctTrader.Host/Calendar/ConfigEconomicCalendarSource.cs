using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using Microsoft.Extensions.Options;

namespace IctTrader.Host.Calendar;

/// <summary>
/// The offline <see cref="IEconomicCalendarSource"/>: serves the operator-supplied events from
/// <c>Ict:Calendar:Events</c> (FOMC/NFP/CPI dates, which are published a year ahead). Read-only by SHAPE — it only
/// returns events, there is no order path (§6.3). This is the default source: no network, fully deterministic, and
/// it makes the §2.5.2 gate verifiable end-to-end.
/// </summary>
internal sealed class ConfigEconomicCalendarSource(IOptions<CalendarFeedOptions> options) : IEconomicCalendarSource
{
    private readonly CalendarFeedOptions _options = options.Value;

    public string Provider => "Config";

    public Task<IReadOnlyList<EconomicEvent>> FetchAsync(
        DateOnly fromNyDate,
        DateOnly toNyDate,
        CancellationToken ct = default)
    {
        IReadOnlyList<EconomicEvent> events = _options.Events
            .Where(e => e.Date >= fromNyDate && e.Date <= toNyDate)
            .Select(e => new EconomicEvent(e.Date, e.Type))
            .ToArray();

        return Task.FromResult(events);
    }
}
