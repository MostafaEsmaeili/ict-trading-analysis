using System.Globalization;
using IctTrader.MarketData.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IctTrader.MarketData.Infrastructure.Feeds;

/// <summary>
/// A <b>read-only</b> OANDA historical-candle fetcher for the backtest pipeline (issue #100). It walks the OANDA
/// v20 candles endpoint BACKWARD in time — OANDA caps a single request at <see cref="MaxCandlesPerRequest"/> (5000)
/// candles — paginating older-and-older pages until it has gathered up to <c>maxCandles</c> completed candles, then
/// returns them in chronological (ascending open-time) order for <see cref="CandleCsvWriter"/> to persist.
/// <para>
/// <b>Read-only is structural (the NON-NEGOTIABLE guardrail):</b> this type issues ONLY HTTP <c>GET</c>s against the
/// candles endpoint (<c>?price=M</c> — mid candles, no bid/ask routing surface) and never writes anything but a local
/// CSV (done by the writer, not here). There is no order/trade/buy/sell path anywhere. The supplied
/// <see cref="HttpClient"/> carries the practice base URL + <c>Authorization: Bearer</c> token (injected by the
/// caller; never committed). Parsing is delegated to the shared <see cref="OandaCandleParser"/> — no duplication.
/// </para>
/// </summary>
public sealed class OandaHistoryFetcher
{
    /// <summary>The OANDA v20 ceiling on candles returned by a single request — the backward-pagination page size.</summary>
    public const int MaxCandlesPerRequest = 5000;

    // The OANDA v20 candles endpoint + query — an external API contract, so these literals are unavoidable.
    private const string CandlesPathFormat = "/v3/instruments/{0}/candles";
    private const string MidPricingComponent = "M";   // ?price=M → mid-price candles (no bid/ask routing surface)

    // OANDA's `to` is an RFC3339 timestamp; it returns the page of candles ENDING at (exclusive of) `to`. The 100ns
    // tick form (7 fractional digits) is the precision .NET round-trips and OANDA accepts.
    private const string OandaTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffffff'Z'";

    private readonly HttpClient _httpClient;
    private readonly ILogger<OandaHistoryFetcher> _logger;

    public OandaHistoryFetcher(HttpClient httpClient, ILogger<OandaHistoryFetcher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _logger = logger ?? NullLogger<OandaHistoryFetcher>.Instance;
    }

    /// <summary>
    /// Fetches up to <paramref name="maxCandles"/> completed candles for <paramref name="instrument"/> at
    /// <paramref name="granularity"/>, paginating backward in time. The first request takes the latest page
    /// (<c>count=5000</c>); each subsequent request takes the page ENDING at the oldest candle gathered so far
    /// (<c>to=&lt;oldest&gt;&amp;count=5000</c>). Pages are deduped by <see cref="CandleDto.OpenTimeUtc"/> and the
    /// walk stops when it has enough candles, a page returns nothing (no more history), or a page adds no new
    /// candle (no progress — the infinite-loop guard). Returns the gathered set sorted chronologically.
    /// </summary>
    public async Task<IReadOnlyList<CandleDto>> FetchAsync(
        string instrument,
        string granularity,
        int maxCandles,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrument);
        ArgumentException.ThrowIfNullOrWhiteSpace(granularity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCandles);

        // Keyed by open-time so a boundary candle shared by two adjacent pages (OANDA's `to` is exclusive but the
        // page edges can still overlap) is deduped — one canonical candle per open time.
        var byOpenTime = new SortedDictionary<DateTimeOffset, CandleDto>();
        DateTimeOffset? oldestSoFar = null;

        while (byOpenTime.Count < maxCandles)
        {
            ct.ThrowIfCancellationRequested();

            var page = await FetchPageAsync(instrument, granularity, oldestSoFar, ct).ConfigureAwait(false);
            if (page.Length == 0)
            {
                _logger.LogInformation(
                    "OANDA history for {Instrument} {Granularity}: page returned 0 candles — reached the start of history.",
                    instrument, granularity);
                break;   // no more history
            }

            var addedThisPage = 0;
            foreach (var candle in page)
            {
                if (byOpenTime.TryAdd(candle.OpenTimeUtc, candle))
                {
                    addedThisPage++;
                }
            }

            if (addedThisPage == 0)
            {
                // Every candle this page returned was already seen — no progress. Stop rather than request the same
                // `to` page forever (the infinite-loop guard).
                _logger.LogInformation(
                    "OANDA history for {Instrument} {Granularity}: page added no new candles — stopping (no progress).",
                    instrument, granularity);
                break;
            }

            // The dictionary is sorted, so the first key is the oldest open time gathered — the next page's `to`.
            oldestSoFar = byOpenTime.Keys.First();
            _logger.LogInformation(
                "OANDA history for {Instrument} {Granularity}: fetched {Count} candles, oldest {Oldest:O}.",
                instrument, granularity, byOpenTime.Count, oldestSoFar.Value);
        }

        // Sorted ascending by construction; cap to the requested count (a final page can overshoot maxCandles) and
        // return the oldest `maxCandles` so the most-recent history is kept contiguous.
        var ordered = byOpenTime.Values.ToList();
        if (ordered.Count > maxCandles)
        {
            ordered = ordered.GetRange(ordered.Count - maxCandles, maxCandles);
        }

        return ordered;
    }

    /// <summary>
    /// Fetches one page: the latest <see cref="MaxCandlesPerRequest"/> candles when <paramref name="toExclusive"/>
    /// is null, otherwise the page of <see cref="MaxCandlesPerRequest"/> candles ENDING at (exclusive of) it.
    /// </summary>
    private async Task<CandleDto[]> FetchPageAsync(
        string instrument,
        string granularity,
        DateTimeOffset? toExclusive,
        CancellationToken ct)
    {
        var requestUri = BuildPageUri(instrument, granularity, toExclusive);

        using var response = await _httpClient.GetAsync(requestUri, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return OandaCandleParser.Parse(json);
    }

    /// <summary>Builds the read-only candles GET for one backward page (granularity + mid + 5000 count [+ to]).</summary>
    private static Uri BuildPageUri(string instrument, string granularity, DateTimeOffset? toExclusive)
    {
        var path = string.Format(CultureInfo.InvariantCulture, CandlesPathFormat, Uri.EscapeDataString(instrument));
        var count = MaxCandlesPerRequest.ToString(CultureInfo.InvariantCulture);
        var query =
            $"?granularity={Uri.EscapeDataString(granularity)}&count={count}&price={MidPricingComponent}";

        if (toExclusive is { } to)
        {
            var toParam = to.UtcDateTime.ToString(OandaTimeFormat, CultureInfo.InvariantCulture);
            query += $"&to={Uri.EscapeDataString(toParam)}";
        }

        return new Uri(path + query, UriKind.Relative);
    }
}
