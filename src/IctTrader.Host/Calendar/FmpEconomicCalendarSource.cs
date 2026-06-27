using System.Globalization;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using Microsoft.Extensions.Options;

namespace IctTrader.Host.Calendar;

/// <summary>
/// The Financial Modeling Prep <see cref="IEconomicCalendarSource"/>: GETs the economic-calendar API over the
/// from/to NY-date window and parses it with <see cref="FmpCalendarParser"/>. Read-only by SHAPE — it issues ONLY a
/// GET against the calendar endpoint; there is no order/broker surface (§6.3). The typed <see cref="HttpClient"/>
/// (base URL) is configured in <c>EconomicCalendarRegistration</c>; the api-key is an FMP query parameter supplied
/// via env, never committed.
/// </summary>
internal sealed class FmpEconomicCalendarSource(HttpClient httpClient, IOptions<CalendarFeedOptions> options)
    : IEconomicCalendarSource
{
    private const string CalendarPath = "/api/v3/economic_calendar";

    private readonly string _apiKey = options.Value.Fmp.ApiKey ?? string.Empty;

    public string Provider => "Fmp";

    public async Task<IReadOnlyList<EconomicEvent>> FetchAsync(
        DateOnly fromNyDate,
        DateOnly toNyDate,
        CancellationToken ct = default)
    {
        var from = fromNyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = toNyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // A read-only GET (FMP carries the key as a query param); the response is parsed by the pure FmpCalendarParser.
        var url = $"{CalendarPath}?from={from}&to={to}&apikey={Uri.EscapeDataString(_apiKey)}";
        var json = await httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
        return FmpCalendarParser.Parse(json);
    }
}
