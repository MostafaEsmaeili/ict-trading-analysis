using FluentAssertions;
using IctTrader.MarketData.Application.Chart;
using IctTrader.MarketData.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.UnitTests.MarketData;

/// <summary>
/// Locks the MarketData chart read-model (plan §9.1): the bounded per-(symbol, timeframe) candle store the
/// dashboard's ICT Pattern Chart serves from. Candles return in CHRONOLOGICAL (oldest→newest) order — what
/// lightweight-charts requires — respect <c>max</c>, evict past the per-series cap, and isolate series. A bus
/// wiring test composes the in-memory bus + <c>AddMarketDataReadModels</c> exactly as the Host does, publishes
/// <see cref="CandleIngested"/>, and queries <see cref="GetChartCandlesQuery"/> to prove the chart serves REAL
/// bars over the bus.
/// </summary>
public sealed class ChartCandleStoreTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 15, 7, 0, 0, TimeSpan.Zero);

    private static CandleDto Candle(string symbol, string timeframe, int minute, decimal close = 1.10m) =>
        new(symbol, timeframe, T0.AddMinutes(minute),
            Open: 1.10m, High: 1.11m, Low: 1.09m, Close: close, Volume: 100m);

    [Fact]
    public void Recent_returns_candles_in_chronological_order()
    {
        var store = new ChartCandleStore();

        // Appended in chronological order; Recent must return oldest→newest.
        store.Append(Candle("EURUSD", "M5", minute: 0, close: 1.10m));
        store.Append(Candle("EURUSD", "M5", minute: 5, close: 1.11m));
        store.Append(Candle("EURUSD", "M5", minute: 10, close: 1.12m));

        var recent = store.Recent("EURUSD", "M5", 10);

        recent.Select(c => c.Close).Should().Equal(1.10m, 1.11m, 1.12m);
    }

    [Fact]
    public void Recent_caps_the_window_to_max_keeping_the_newest_chronologically()
    {
        var store = new ChartCandleStore();
        for (var i = 0; i < 5; i++)
        {
            store.Append(Candle("EURUSD", "M5", minute: i, close: 1.10m + (i * 0.01m)));
        }

        // The two most-recent candles, still oldest→newest within the window.
        var recent = store.Recent("EURUSD", "M5", 2);

        recent.Select(c => c.Close).Should().Equal(1.13m, 1.14m);
    }

    [Fact]
    public void Recent_evicts_the_oldest_past_the_per_series_cap()
    {
        var store = new ChartCandleStore();

        var total = ChartCandleStore.MaxCandlesPerSeries + 50;
        for (var i = 0; i < total; i++)
        {
            store.Append(Candle("EURUSD", "M5", minute: i));
        }

        var all = store.Recent("EURUSD", "M5", int.MaxValue);
        all.Should().HaveCount(ChartCandleStore.MaxCandlesPerSeries);

        // The survivors are the NEWEST cap candles; the oldest 50 were evicted. First/last open times bound it.
        all[0].OpenTimeUtc.Should().Be(T0.AddMinutes(total - ChartCandleStore.MaxCandlesPerSeries));
        all[^1].OpenTimeUtc.Should().Be(T0.AddMinutes(total - 1));
    }

    [Fact]
    public void Series_are_isolated_by_symbol_and_timeframe()
    {
        var store = new ChartCandleStore();

        store.Append(Candle("EURUSD", "M5", minute: 0, close: 1.10m));
        store.Append(Candle("EURUSD", "M15", minute: 0, close: 2.20m));
        store.Append(Candle("GBPUSD", "M5", minute: 0, close: 3.30m));

        store.Recent("EURUSD", "M5", 10).Select(c => c.Close).Should().Equal(1.10m);
        store.Recent("EURUSD", "M15", 10).Select(c => c.Close).Should().Equal(2.20m);
        store.Recent("GBPUSD", "M5", 10).Select(c => c.Close).Should().Equal(3.30m);
    }

    [Fact]
    public void Recent_matches_symbol_and_timeframe_case_insensitively()
    {
        var store = new ChartCandleStore();
        store.Append(Candle("EURUSD", "M5", minute: 0, close: 1.10m));

        store.Recent("eurusd", "m5", 10).Select(c => c.Close).Should().Equal(1.10m);
    }

    [Fact]
    public void A_non_positive_max_or_unknown_series_returns_an_empty_window()
    {
        var store = new ChartCandleStore();
        store.Append(Candle("EURUSD", "M5", minute: 0));

        store.Recent("EURUSD", "M5", 0).Should().BeEmpty();
        store.Recent("EURUSD", "M5", -1).Should().BeEmpty();
        store.Recent("EURUSD", "M15", 10).Should().BeEmpty(); // unknown timeframe
        store.Recent("USDJPY", "M5", 10).Should().BeEmpty();   // unknown symbol
    }

    [Fact]
    public async Task The_chart_candles_query_serves_real_bars_over_the_bus()
    {
        var services = new ServiceCollection();
        services.AddMessaging(typeof(ChartCandleProjectionHandler).Assembly);
        services.AddMarketDataReadModels();
        using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new CandleIngested(Candle("EURUSD", "M5", minute: 0, close: 1.10m)));
        await bus.PublishAsync(new CandleIngested(Candle("EURUSD", "M5", minute: 5, close: 1.11m)));

        var candles = await bus.QueryAsync(new GetChartCandlesQuery("EURUSD", "M5", 500));

        candles.Select(c => c.Close).Should().Equal(1.10m, 1.11m); // chronological, both bars
    }
}
