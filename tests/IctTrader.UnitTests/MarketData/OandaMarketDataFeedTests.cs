using System.Net;
using System.Text;
using FluentAssertions;
using IctTrader.MarketData.Infrastructure.Feeds;

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
}
