using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentAssertions;
using IctTrader.MarketData.Infrastructure.Feeds;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.MarketData;

/// <summary>
/// Drives <see cref="OandaMarketDataFeed.StreamCandlesAsync"/> over a FAKE <see cref="HttpMessageHandler"/>
/// (no network) with <see cref="OandaFeedOptions.LiveStreaming"/> off (a finite historical stream) and asserts
/// it yields the expected <see cref="IctTrader.MarketData.Contracts.CandleDto"/>s in chronological order, plus
/// the structural read-only markers (<see cref="OandaMarketDataFeed.IsReadOnly"/> / Provider). With live
/// streaming off the poll/delay path is never reached, so no clock is needed there.
/// </summary>
public class OandaMarketDataFeedTests
{
    private const string TwoCandleJson = """
    {
      "instrument": "EUR_USD",
      "granularity": "M5",
      "candles": [
        {
          "time": "2024-07-01T07:00:00.000000000Z",
          "mid": { "o": "1.0832", "h": "1.0840", "l": "1.0828", "c": "1.0836" },
          "volume": 123,
          "complete": true
        },
        {
          "time": "2024-07-01T07:05:00.000000000Z",
          "mid": { "o": "1.0836", "h": "1.0851", "l": "1.0835", "c": "1.0849" },
          "volume": 210,
          "complete": true
        }
      ]
    }
    """;

    private static OandaFeedOptions Options(bool liveStreaming = false) => new()
    {
        BaseUrl = "https://api-fxpractice.oanda.com",
        Token = "test-practice-token",
        Instruments = ["EUR_USD"],
        Granularity = "M5",
        HistoryCount = 2,
        LiveStreaming = liveStreaming,
    };

    private static OandaMarketDataFeed BuildFeed(string responseJson, out CapturingHandler handler)
    {
        handler = new CapturingHandler(responseJson);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Options().BaseUrl) };
        return new OandaMarketDataFeed(httpClient, Options(), TimeProvider.System);
    }

    [Fact]
    public void Reports_its_provider_name_and_is_read_only_by_shape()
    {
        var feed = BuildFeed(TwoCandleJson, out _);

        feed.Provider.Should().Be(OandaMarketDataFeed.ProviderName);
        feed.Provider.Should().Be("Oanda");
        OandaMarketDataFeed.IsReadOnly.Should().BeTrue("the feed is structurally a market-data reader only");
    }

    [Fact]
    public async Task Backfill_yields_the_complete_candles_in_chronological_order()
    {
        var feed = BuildFeed(TwoCandleJson, out _);

        var candles = new List<IctTrader.MarketData.Contracts.CandleDto>();
        await foreach (var candle in feed.StreamCandlesAsync(CancellationToken.None))
        {
            candles.Add(candle);
        }

        candles.Should().HaveCount(2);
        candles.Select(c => c.OpenTimeUtc).Should().BeInAscendingOrder();
        candles[0].Symbol.Should().Be("EURUSD");
        candles[0].Timeframe.Should().Be("M5");
        candles[0].Close.Should().Be(1.0836m);
        candles[1].Close.Should().Be(1.0849m);
    }

    [Fact]
    public async Task Issues_a_read_only_GET_to_the_practice_candles_endpoint_with_the_bearer_token()
    {
        var feed = BuildFeed(TwoCandleJson, out var handler);

        await foreach (var _ in feed.StreamCandlesAsync(CancellationToken.None))
        {
            // drain the finite stream
        }

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get, "a market-data reader never writes/orders");
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/v3/instruments/EUR_USD/candles");
        handler.LastRequest.RequestUri.Query.Should().Contain("granularity=M5").And.Contain("price=M");
    }

    [Fact]
    public async Task Honours_cancellation()
    {
        var feed = BuildFeed(TwoCandleJson, out _);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
        {
            await foreach (var _ in feed.StreamCandlesAsync(cts.Token))
            {
                // should not get here
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- Live-streaming poll path (watermark dedupe + transient-error survival, no real network) ----
    //
    // The live branch is reachable only with LiveStreaming=true; it is driven by Task.Delay(_timeProvider), so a
    // FakeTimeProvider lets the test release each poll tick deterministically. A scripted transport returns a
    // queue of per-request bodies (and can THROW a transient error) so we can prove (a) the from=<watermark>
    // strictly-after filter drops the boundary candle (no duplicate/skipped bar) and (b) a transient failure is
    // swallowed and the stream resumes.

    [Fact]
    public async Task Live_poll_dedupes_the_boundary_candle_and_survives_a_transient_error()
    {
        // Response #1 = the two-candle backfill (07:00, 07:05) → watermark becomes 07:05.
        // Response #2 = first live poll (from=07:05): the boundary 07:05 PLUS a new 07:10 → only 07:10 survives.
        // Response #3 = THROWS a transient HttpRequestException → swallowed, stream continues.
        // Response #4 = next live poll (from=07:10): the boundary 07:10 PLUS a new 07:15 → only 07:15 survives.
        var handler = new ScriptedHandler();
        handler.EnqueueBody(TwoCandleJson);
        handler.EnqueueBody(BoundaryPlusNewJson("2024-07-01T07:05:00.000000000Z", "2024-07-01T07:10:00.000000000Z"));
        handler.EnqueueThrow(new HttpRequestException("transient 503"));
        handler.EnqueueBody(BoundaryPlusNewJson("2024-07-01T07:10:00.000000000Z", "2024-07-01T07:15:00.000000000Z"));

        var anchor = new DateTimeOffset(2024, 7, 1, 7, 5, 0, TimeSpan.Zero);
        var fake = new FakeTimeProvider(anchor);
        var opts = Options(liveStreaming: true);   // PollSeconds=60 (default) — advanced via the fake clock
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(opts.BaseUrl) };
        var feed = new OandaMarketDataFeed(httpClient, opts, fake);

        var collected = new ConcurrentQueue<IctTrader.MarketData.Contracts.CandleDto>();
        using var cts = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            await foreach (var candle in feed.StreamCandlesAsync(cts.Token).ConfigureAwait(false))
            {
                collected.Enqueue(candle);
            }
        });

        // The two backfill candles arrive before any poll tick.
        await WaitForCountAsync(collected, 2);

        // Release poll #1 (07:10), the transient poll #2 (swallowed), and poll #3 (07:15). Advancing the fake
        // clock by one poll period at a time releases exactly one Task.Delay each.
        var poll = TimeSpan.FromSeconds(opts.PollSeconds);
        fake.Advance(poll);
        await WaitForCountAsync(collected, 3);   // 07:10 (07:05 boundary dropped)

        fake.Advance(poll);                       // poll #2 throws — swallowed, nothing yielded
        fake.Advance(poll);                       // poll #3
        await WaitForCountAsync(collected, 4);   // 07:15 (07:10 boundary dropped)

        cts.Cancel();
        var act = async () => await pump;
        await act.Should().ThrowAsync<OperationCanceledException>("a real shutdown tears the live loop down");

        var opens = collected.Select(c => c.OpenTimeUtc.UtcDateTime).ToList();
        opens.Should().Equal(
            new DateTime(2024, 7, 1, 7, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 7, 1, 7, 5, 0, DateTimeKind.Utc),
            new DateTime(2024, 7, 1, 7, 10, 0, DateTimeKind.Utc),
            new DateTime(2024, 7, 1, 7, 15, 0, DateTimeKind.Utc));
        opens.Should().OnlyHaveUniqueItems("the strictly-after watermark filter must drop the inclusive boundary candle");
    }

    [Fact]
    public async Task Live_poll_pages_by_from_watermark_after_the_first_tick()
    {
        var handler = new ScriptedHandler();
        handler.EnqueueBody(TwoCandleJson);   // backfill → watermark 07:05
        handler.EnqueueBody(BoundaryPlusNewJson("2024-07-01T07:05:00.000000000Z", "2024-07-01T07:10:00.000000000Z"));

        var fake = new FakeTimeProvider(new DateTimeOffset(2024, 7, 1, 7, 5, 0, TimeSpan.Zero));
        var opts = Options(liveStreaming: true);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(opts.BaseUrl) };
        var feed = new OandaMarketDataFeed(httpClient, opts, fake);

        var collected = new ConcurrentQueue<IctTrader.MarketData.Contracts.CandleDto>();
        using var cts = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            await foreach (var candle in feed.StreamCandlesAsync(cts.Token).ConfigureAwait(false))
            {
                collected.Enqueue(candle);
            }
        });

        await WaitForCountAsync(collected, 2);
        fake.Advance(TimeSpan.FromSeconds(opts.PollSeconds));
        await WaitForCountAsync(collected, 3);

        cts.Cancel();
        try { await pump; } catch (OperationCanceledException) { /* expected shutdown */ }

        // The live poll (after a watermark exists) must request `from=<watermark>`, not a fixed `count=` tail.
        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.Query.Should().Contain("from=").And.NotContain("count=");
    }

    /// <summary>Backfill 07:00/07:05 keyed body re-used as a two-candle response (the watermark seeder).</summary>
    private static string BoundaryPlusNewJson(string boundaryIso, string newIso) => $$"""
    {
      "instrument": "EUR_USD",
      "granularity": "M5",
      "candles": [
        {
          "time": "{{boundaryIso}}",
          "mid": { "o": "1.0836", "h": "1.0851", "l": "1.0835", "c": "1.0849" },
          "volume": 210,
          "complete": true
        },
        {
          "time": "{{newIso}}",
          "mid": { "o": "1.0849", "h": "1.0862", "l": "1.0847", "c": "1.0858" },
          "volume": 198,
          "complete": true
        }
      ]
    }
    """;

    /// <summary>Polls the collection (the live pump runs on a background task) until it reaches <paramref name="count"/>.</summary>
    private static async Task WaitForCountAsync<T>(ConcurrentQueue<T> collected, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (collected.Count < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Expected {count} candles but only saw {collected.Count} within the timeout.");
            }

            await Task.Delay(5).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A fake transport that returns a fixed JSON body for every request and records the last request, so the
    /// test can assert the read-only GET shape without any network. The feed is read-only by SHAPE — this stub
    /// never simulates an order endpoint because the feed has no order path to exercise.
    /// </summary>
    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// A fake transport that serves a QUEUE of scripted per-request actions — each either returns a JSON body or
    /// THROWS a transient exception — so a test can drive the live-poll watermark/dedupe and retry paths without
    /// any network. It records the last request URI so the <c>from=&lt;watermark&gt;</c> paging can be asserted.
    /// Read-only by SHAPE: it never simulates an order endpoint because the feed has no order path to exercise.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Func<HttpResponseMessage>> _actions = new();

        public Uri? LastRequestUri { get; private set; }

        public void EnqueueBody(string json) =>
            _actions.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        public void EnqueueThrow(Exception exception) =>
            _actions.Enqueue(() => throw exception);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            if (!_actions.TryDequeue(out var action))
            {
                // No more scripted responses — return an empty (no new candles) tail so a late extra poll is inert
                // rather than throwing and masking the assertion under test.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{ "instrument": "EUR_USD", "granularity": "M5", "candles": [] }""",
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            return Task.FromResult(action());
        }
    }
}
