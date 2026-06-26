using System.Runtime.CompilerServices;
using FluentAssertions;
using IctTrader.MarketData.Application.Abstractions;
using IctTrader.MarketData.Application.Ingestion;
using IctTrader.MarketData.Contracts;
using IctTrader.MarketData.Infrastructure.Feeds;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.UnitTests.MarketData;

/// <summary>
/// Locks the read-only feed + ingestion (plan §4.1/§6.1/§6.3): the Replay feed streams candles in
/// chronological order, ingestion publishes one <see cref="CandleIngested"/> per candle through the real
/// bus, and a non-read-only feed is refused (the structural no-live-trading guardrail).
/// </summary>
public class MarketDataIngestionTests
{
    private static CandleDto Candle(string symbol, DateTimeOffset openUtc, decimal close = 1.10m) =>
        new(symbol, "M5", openUtc, Open: 1.10m, High: 1.11m, Low: 1.09m, Close: close, Volume: 100m);

    private static readonly DateTimeOffset T0 = new(2024, 1, 15, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Replay_feed_is_read_only_and_named()
    {
        var feed = new ReplayMarketDataFeed([Candle("EURUSD", T0)]);

        feed.IsReadOnly.Should().BeTrue();
        feed.Provider.Should().Be(ReplayMarketDataFeed.ProviderName);
    }

    [Fact]
    public async Task Replay_feed_streams_candles_in_chronological_order()
    {
        // Supplied out of order — the feed must sort to chronological so a replay is reproducible.
        var feed = new ReplayMarketDataFeed(
        [
            Candle("EURUSD", T0.AddMinutes(10), close: 1.12m),
            Candle("EURUSD", T0, close: 1.10m),
            Candle("EURUSD", T0.AddMinutes(5), close: 1.11m),
        ]);

        var streamed = new List<DateTimeOffset>();
        await foreach (var candle in feed.StreamCandlesAsync())
        {
            streamed.Add(candle.OpenTimeUtc);
        }

        streamed.Should().Equal(T0, T0.AddMinutes(5), T0.AddMinutes(10));
    }

    [Fact]
    public async Task Ingestor_publishes_one_CandleIngested_per_candle_in_order()
    {
        var captured = new CapturedCandles();
        using var sp = BuildBus(captured);
        var feed = new ReplayMarketDataFeed(
        [
            Candle("EURUSD", T0, close: 1.10m),
            Candle("EURUSD", T0.AddMinutes(5), close: 1.11m),
        ]);
        var ingestor = new MarketDataIngestor(feed, sp.GetRequiredService<IMessageBus>());

        await ingestor.IngestAsync();

        captured.Closes.Should().Equal(1.10m, 1.11m);   // both, in chronological order
    }

    [Fact]
    public async Task Ingestor_refuses_a_feed_that_is_not_read_only()
    {
        var captured = new CapturedCandles();
        using var sp = BuildBus(captured);
        var ingestor = new MarketDataIngestor(new WritableFeed(), sp.GetRequiredService<IMessageBus>());

        var ingest = async () => await ingestor.IngestAsync();

        await ingest.Should().ThrowAsync<InvalidOperationException>();
        captured.Closes.Should().BeEmpty();   // refused before any publish
    }

    private static ServiceProvider BuildBus(CapturedCandles captured)
    {
        var services = new ServiceCollection();
        services.AddSingleton(captured);
        services.AddScoped<IEventHandler<CandleIngested>, CapturingCandleHandler>();
        services.AddMessaging();
        return services.BuildServiceProvider();
    }

    private sealed class CapturedCandles
    {
        public List<decimal> Closes { get; } = [];
    }

    private sealed class CapturingCandleHandler(CapturedCandles captured) : IEventHandler<CandleIngested>
    {
        public Task HandleAsync(CandleIngested @event, CancellationToken cancellationToken = default)
        {
            captured.Closes.Add(@event.Candle.Close);
            return Task.CompletedTask;
        }
    }

    private sealed class WritableFeed : IMarketDataFeed
    {
        public string Provider => "Writable";
        public bool IsReadOnly => false;   // deliberately illegal — the ingestor must refuse it

        public async IAsyncEnumerable<CandleDto> StreamCandlesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return Candle("EURUSD", T0);
            await Task.CompletedTask;
        }
    }
}
