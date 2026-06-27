using FluentAssertions;
using IctTrader.MarketData.Application.Ingestion;
using IctTrader.MarketData.Contracts;
using IctTrader.MarketData.Infrastructure.Feeds;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.UnitTests.MarketData;

/// <summary>
/// Locks the read-only feed + ingestion (plan §4.1/§6.1): the Replay feed streams candles in chronological
/// order and ingestion publishes one <see cref="CandleIngested"/> per candle through the real bus. Read-only
/// is structural — <see cref="IctTrader.MarketData.Application.Abstractions.IMarketDataFeed"/> exposes no
/// write/order method — so there is no writable-feed state left to test.
/// </summary>
public class MarketDataIngestionTests
{
    private static CandleDto Candle(string symbol, DateTimeOffset openUtc, decimal close = 1.10m) =>
        new(symbol, "M5", openUtc, Open: 1.10m, High: 1.11m, Low: 1.09m, Close: close, Volume: 100m);

    private static readonly DateTimeOffset T0 = new(2024, 1, 15, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Replay_feed_reports_its_provider_name()
    {
        var feed = new ReplayMarketDataFeed([Candle("EURUSD", T0)]);

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
    public async Task One_candle_whose_publish_throws_does_not_abort_ingestion_of_the_good_candles_around_it()
    {
        // A transient/data-shape fault on ONE bar (here: a handler that throws on a sentinel "bad" candle) must NOT
        // tear down a long-running stream — the offending bar is logged and skipped, and the surrounding good candles
        // are still published. This mirrors the OANDA live-poll transient-error isolation (plan §6.3).
        var captured = new CapturedCandles();
        using var sp = BuildBus(captured, throwOnClose: 9.99m);
        var feed = new ReplayMarketDataFeed(
        [
            Candle("EURUSD", T0, close: 1.10m),                 // good
            Candle("EURUSD", T0.AddMinutes(5), close: 9.99m),   // the bad bar — its publish throws
            Candle("EURUSD", T0.AddMinutes(10), close: 1.12m),  // good — must still arrive
        ]);
        var ingestor = new MarketDataIngestor(feed, sp.GetRequiredService<IMessageBus>());

        // Ingestion completes WITHOUT propagating the per-candle fault (no exception escapes IngestAsync).
        await ingestor.IngestAsync();

        // The two good candles are still published, in order; only the bad bar is dropped.
        captured.Closes.Should().Equal(1.10m, 1.12m);
    }

    [Fact]
    public async Task A_run_of_consecutive_publish_failures_aborts_the_stream_so_a_deterministic_bug_surfaces()
    {
        // A handler that throws on EVERY candle is a deterministic bug, not a transient blip. The circuit breaker
        // must abort (rethrow to the hosted service's Error boundary) once MaxConsecutivePublishFailures is reached,
        // instead of demoting the bug to per-candle Warning noise + an "empty but successful" run.
        var captured = new CapturedCandles();
        using var sp = BuildBus(captured, alwaysThrow: true);
        var feed = new ReplayMarketDataFeed(
            Enumerable
                .Range(0, MarketDataIngestor.MaxConsecutivePublishFailures + 5)
                .Select(i => Candle("EURUSD", T0.AddMinutes(5 * i)))
                .ToArray());
        var ingestor = new MarketDataIngestor(feed, sp.GetRequiredService<IMessageBus>());

        var act = () => ingestor.IngestAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        // Nothing was ever published successfully (the bug throws on every bar).
        captured.Closes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_clean_publish_resets_the_consecutive_failure_streak()
    {
        // Fewer than MaxConsecutivePublishFailures *consecutive* bad bars, each separated by a good bar, must NOT trip
        // the breaker — a clean publish resets the streak, so isolated transient blips never accumulate to an abort.
        var captured = new CapturedCandles();
        using var sp = BuildBus(captured, throwOnClose: 9.99m);
        var candles = new List<CandleDto>();
        var t = T0;
        for (var i = 0; i < MarketDataIngestor.MaxConsecutivePublishFailures + 10; i++)
        {
            candles.Add(Candle("EURUSD", t, close: 9.99m));       // bad — its publish throws
            t = t.AddMinutes(5);
            candles.Add(Candle("EURUSD", t, close: 1.10m + i));   // good — resets the streak
            t = t.AddMinutes(5);
        }

        var feed = new ReplayMarketDataFeed([.. candles]);
        var ingestor = new MarketDataIngestor(feed, sp.GetRequiredService<IMessageBus>());

        // No abort: the good bars between the bad ones keep the consecutive count at 1.
        await ingestor.IngestAsync();

        // Exactly the good bars arrived (every bad bar isolated, never tripping the breaker).
        captured.Closes.Should().HaveCount(MarketDataIngestor.MaxConsecutivePublishFailures + 10);
    }

    private static ServiceProvider BuildBus(CapturedCandles captured, decimal? throwOnClose = null, bool alwaysThrow = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(captured);
        if (throwOnClose is { } sentinel || alwaysThrow)
        {
            // alwaysThrow → an unmatchable sentinel close is irrelevant; ThrowingMarker.AlwaysThrow drives it.
            services.AddSingleton(new ThrowingMarker(throwOnClose ?? decimal.MinValue, alwaysThrow));
            services.AddScoped<IEventHandler<CandleIngested>, ThrowingCandleHandler>();
        }

        services.AddScoped<IEventHandler<CandleIngested>, CapturingCandleHandler>();
        services.AddMessaging();
        return services.BuildServiceProvider();
    }

    private sealed record ThrowingMarker(decimal BadClose, bool AlwaysThrow = false);

    // Registered BEFORE the capturing handler, so a throw on the bad bar happens during the SAME PublishAsync the
    // ingestor must isolate — proving the isolation is at the ingestor (one bad publish does not abort the stream).
    private sealed class ThrowingCandleHandler(ThrowingMarker marker) : IEventHandler<CandleIngested>
    {
        public Task HandleAsync(CandleIngested @event, CancellationToken cancellationToken = default)
        {
            if (marker.AlwaysThrow || @event.Candle.Close == marker.BadClose)
            {
                throw new InvalidOperationException("Simulated transient downstream fault on one bar.");
            }

            return Task.CompletedTask;
        }
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
}
