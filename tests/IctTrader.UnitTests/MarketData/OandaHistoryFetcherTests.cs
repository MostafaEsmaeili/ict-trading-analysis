using System.Globalization;
using System.Net;
using System.Text;
using FluentAssertions;
using IctTrader.MarketData.Infrastructure.Feeds;

namespace IctTrader.UnitTests.MarketData;

/// <summary>
/// Drives <see cref="OandaHistoryFetcher.FetchAsync"/> over a FAKE <see cref="HttpMessageHandler"/> (no network)
/// that serves the OANDA backward-pagination protocol from a fixed candle universe: the first request (no
/// <c>to</c>) returns the newest block; a request keyed by <c>to=&lt;oldest&gt;</c> returns the older block ENDING
/// at <c>to</c> (with a deliberate boundary-candle overlap to exercise the dedupe); a request past the start of
/// history returns an empty page. Asserts the fetcher paginates, dedupes the boundary candle, stops on the empty
/// page, respects <c>maxCandles</c>, returns chronological order, and issues ONLY read-only GETs.
/// </summary>
public class OandaHistoryFetcherTests
{
    private const string Instrument = "EUR_USD";
    private const string Granularity = "M5";
    private static readonly DateTimeOffset Anchor = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    // The full history universe, oldest → newest. Page 1 serves the newest `pageSize`; page 2 (keyed by `to`)
    // serves the older block ending at `to` (inclusive of the boundary candle, so it overlaps page 1's oldest).
    private const int PageSize = 6;        // small stand-in for the 5000 cap so the test stays readable
    private const int UniverseSize = 11;   // > one page so a second page is required; < two full pages

    [Fact]
    public async Task Paginates_backward_dedupes_the_boundary_and_returns_chronological_order()
    {
        var handler = new PagingHandler(BuildUniverse(UniverseSize), PageSize);
        var fetcher = BuildFetcher(handler);

        var candles = await fetcher.FetchAsync(Instrument, Granularity, maxCandles: UniverseSize, CancellationToken.None);

        // The whole universe, deduped (the boundary candle shared by the two pages appears once) and ascending.
        candles.Should().HaveCount(UniverseSize);
        candles.Select(c => c.OpenTimeUtc).Should().BeInAscendingOrder();
        candles.Select(c => c.OpenTimeUtc).Should().OnlyHaveUniqueItems("the boundary candle must be deduped");
        candles[0].OpenTimeUtc.Should().Be(Anchor);
        candles[^1].OpenTimeUtc.Should().Be(Anchor + Step * (UniverseSize - 1));
        candles[0].Symbol.Should().Be("EURUSD", "the parser normalises EUR_USD → EURUSD");
        candles[0].Timeframe.Should().Be(Granularity);
    }

    [Fact]
    public async Task Stops_on_the_empty_page_when_history_is_exhausted()
    {
        var handler = new PagingHandler(BuildUniverse(UniverseSize), PageSize);
        var fetcher = BuildFetcher(handler);

        // Ask for far more than exist — the fetcher must stop on the empty page, not loop forever.
        var candles = await fetcher.FetchAsync(Instrument, Granularity, maxCandles: 10_000, CancellationToken.None);

        candles.Should().HaveCount(UniverseSize);
        handler.Requests.Should().Contain(uri => uri.Query.Contains("to=", StringComparison.Ordinal),
            "a second backward page must have been requested");
    }

    [Fact]
    public async Task Respects_maxCandles_and_keeps_the_most_recent_history()
    {
        var handler = new PagingHandler(BuildUniverse(UniverseSize), PageSize);
        var fetcher = BuildFetcher(handler);

        const int max = 4;
        var candles = await fetcher.FetchAsync(Instrument, Granularity, maxCandles: max, CancellationToken.None);

        candles.Should().HaveCount(max);
        candles.Select(c => c.OpenTimeUtc).Should().BeInAscendingOrder();
        // The newest `max` candles are kept contiguous (a single page already satisfies the budget).
        candles[^1].OpenTimeUtc.Should().Be(Anchor + Step * (UniverseSize - 1));
    }

    [Fact]
    public async Task Issues_only_read_only_GETs_to_the_practice_candles_endpoint()
    {
        var handler = new PagingHandler(BuildUniverse(UniverseSize), PageSize);
        var fetcher = BuildFetcher(handler);

        await fetcher.FetchAsync(Instrument, Granularity, maxCandles: UniverseSize, CancellationToken.None);

        handler.Methods.Should().OnlyContain(m => m == HttpMethod.Get, "a history fetcher never writes/orders");
        handler.Requests.Should().NotBeEmpty();
        handler.Requests.Should().OnlyContain(uri => uri.AbsolutePath == "/v3/instruments/EUR_USD/candles");
        handler.Requests.Should().OnlyContain(uri =>
            uri.Query.Contains("granularity=M5", StringComparison.Ordinal)
            && uri.Query.Contains("price=M", StringComparison.Ordinal)
            && uri.Query.Contains("count=5000", StringComparison.Ordinal));
    }

    private static OandaHistoryFetcher BuildFetcher(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api-fxpractice.oanda.com") };
        return new OandaHistoryFetcher(httpClient);
    }

    /// <summary>The candle universe, oldest → newest, evenly stepped from <see cref="Anchor"/>.</summary>
    private static IReadOnlyList<(DateTimeOffset Time, decimal Price)> BuildUniverse(int size)
    {
        var universe = new List<(DateTimeOffset, decimal)>(size);
        for (var i = 0; i < size; i++)
        {
            universe.Add((Anchor + Step * i, 1.0800m + 0.0001m * i));
        }

        return universe;
    }

    /// <summary>
    /// A fake transport that serves the OANDA backward-pagination protocol from a fixed candle universe and records
    /// every request method + URI. Read-only by SHAPE — it never simulates an order endpoint because the fetcher has
    /// no order path to exercise.
    /// </summary>
    private sealed class PagingHandler(IReadOnlyList<(DateTimeOffset Time, decimal Price)> universe, int pageSize)
        : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            Requests.Add(request.RequestUri!);

            var to = ParseTo(request.RequestUri!.Query);
            var page = SlicePage(to);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildJson(page), Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }

        /// <summary>
        /// Returns the page of up to <see cref="pageSize"/> candles ENDING at <paramref name="to"/>. With no
        /// <c>to</c> it is the newest page. The boundary candle AT <c>to</c> is INCLUDED — that overlap with the
        /// previous page's oldest candle is exactly what the fetcher must dedupe. Once <c>to</c> has walked back to
        /// (or below) the oldest candle in the universe, the page is EMPTY — history is exhausted (the stop signal).
        /// </summary>
        private List<(DateTimeOffset Time, decimal Price)> SlicePage(DateTimeOffset? to)
        {
            if (to is null)
            {
                // First page: the newest `pageSize` of the whole universe.
                return TakeNewest(universe.ToList());
            }

            var cutoff = to.Value;
            var oldest = universe[0].Time;

            // `to` has reached the start of history — nothing older remains to return.
            if (cutoff <= oldest)
            {
                return [];   // empty page → the fetcher stops
            }

            // Inclusive boundary: candles at-or-before `to`, so the candle AT `to` overlaps the prior page (dedupe).
            return TakeNewest(universe.Where(c => c.Time <= cutoff).ToList());
        }

        private List<(DateTimeOffset Time, decimal Price)> TakeNewest(List<(DateTimeOffset Time, decimal Price)> eligible)
        {
            if (eligible.Count == 0)
            {
                return [];
            }

            var take = Math.Min(pageSize, eligible.Count);
            return eligible.GetRange(eligible.Count - take, take);   // the newest `take` (still chronological)
        }

        private static DateTimeOffset? ParseTo(string query)
        {
            const string key = "to=";
            var index = query.IndexOf(key, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            var start = index + key.Length;
            var end = query.IndexOf('&', start);
            var raw = end < 0 ? query[start..] : query[start..end];
            var decoded = Uri.UnescapeDataString(raw);

            return DateTimeOffset.Parse(
                decoded,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        private static string BuildJson(IReadOnlyList<(DateTimeOffset Time, decimal Price)> page)
        {
            var builder = new StringBuilder();
            builder.Append("{\"instrument\":\"EUR_USD\",\"granularity\":\"M5\",\"candles\":[");
            for (var i = 0; i < page.Count; i++)
            {
                var (time, price) = page[i];
                var iso = time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
                var p = price.ToString(CultureInfo.InvariantCulture);
                builder.Append(CultureInfo.InvariantCulture,
                    $"{{\"time\":\"{iso}\",\"mid\":{{\"o\":\"{p}\",\"h\":\"{p}\",\"l\":\"{p}\",\"c\":\"{p}\"}},\"volume\":100,\"complete\":true}}");
                if (i < page.Count - 1)
                {
                    builder.Append(',');
                }
            }

            builder.Append("]}");
            return builder.ToString();
        }
    }
}
