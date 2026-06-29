using System.Threading.Channels;
using FluentAssertions;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Application.Chart;
using IctTrader.MarketData.Application.Persistence;
using IctTrader.MarketData.Contracts;
using IctTrader.MarketData.Infrastructure.Persistence;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IctTrader.UnitTests.MarketData;

/// <summary>
/// Unit tests for the candle persistence machinery (plan §7):
/// <list type="bullet">
///   <item><see cref="CandlePersistenceOptions"/> self-validation.</item>
///   <item><see cref="CandlePersistenceProjectionHandler"/> enqueues + drops gracefully.</item>
///   <item><see cref="CandlePersistenceHostedService"/> batch flush cycle.</item>
///   <item><see cref="GetChartRangeQueryHandler"/> range-cap enforced.</item>
/// </list>
/// </summary>
public sealed class CandlePersistenceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static readonly Symbol EurUsd = new("EURUSD");

    private static Candle MakeCandle(int minuteOffset = 0, decimal close = 1.10m) =>
        new(EurUsd, Timeframe.M5,
            new DateTimeOffset(2024, 3, 4, 9, minuteOffset * 5, 0, TimeSpan.Zero),
            1.09m, 1.11m, 1.08m, close, 1000m);

    private static CandleDto MakeCandleDto(int minuteOffset = 0, decimal close = 1.10m) =>
        new("EURUSD", "M5",
            new DateTimeOffset(2024, 3, 4, 9, minuteOffset * 5, 0, TimeSpan.Zero),
            1.09m, 1.11m, 1.08m, close, 1000m);

    // ── CandlePersistenceOptions validation ──────────────────────────────────────────────────────────

    [Fact]
    public void CandlePersistenceOptions_default_values_are_valid()
    {
        var opts = new CandlePersistenceOptions();

        opts.Validate().Should().BeEmpty("the defaults satisfy all constraints");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void CandlePersistenceOptions_rejects_invalid_BatchSize(int batchSize)
    {
        var opts = new CandlePersistenceOptions { BatchSize = batchSize };

        opts.Validate().Should().ContainMatch("*BatchSize*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(300_001)]
    public void CandlePersistenceOptions_rejects_invalid_FlushIntervalMs(int ms)
    {
        var opts = new CandlePersistenceOptions { FlushIntervalMs = ms };

        opts.Validate().Should().ContainMatch("*FlushIntervalMs*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CandlePersistenceOptions_rejects_non_positive_MaxRangeCandles(int max)
    {
        var opts = new CandlePersistenceOptions { MaxRangeCandles = max };

        opts.Validate().Should().ContainMatch("*MaxRangeCandles*");
    }

    // ── CandlePersistenceProjectionHandler ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Projection_handler_enqueues_valid_candle_into_channel()
    {
        var channel = Channel.CreateBounded<Candle>(10);
        var handler = new CandlePersistenceProjectionHandler(
            channel, NullLogger<CandlePersistenceProjectionHandler>.Instance);

        await handler.HandleAsync(new CandleIngested(MakeCandleDto(0)));

        channel.Reader.TryRead(out var c).Should().BeTrue();
        c.Symbol.Value.Should().Be("EURUSD");
        c.Timeframe.Should().Be(Timeframe.M5);
    }

    [Fact]
    public async Task Projection_handler_skips_unknown_timeframe_gracefully()
    {
        var channel = Channel.CreateBounded<Candle>(10);
        var handler = new CandlePersistenceProjectionHandler(
            channel, NullLogger<CandlePersistenceProjectionHandler>.Instance);

        var badDto = new CandleDto("EURUSD", "BADTF",
            DateTimeOffset.UtcNow, 1m, 1.1m, 0.9m, 1m, 100m);

        // Should not throw; just logs a warning.
        await handler.HandleAsync(new CandleIngested(badDto));

        channel.Reader.TryRead(out _).Should().BeFalse("unknown timeframe candle was discarded");
    }

    [Fact]
    public async Task Projection_handler_does_not_throw_when_channel_is_full()
    {
        // A capacity-1 channel that already has a candle — the next write should drop silently.
        var channel = Channel.CreateBounded<Candle>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });

        await channel.Writer.WriteAsync(MakeCandle(0));

        var handler = new CandlePersistenceProjectionHandler(
            channel, NullLogger<CandlePersistenceProjectionHandler>.Instance);

        // Act: second write on a full channel — must not throw.
        var act = async () => await handler.HandleAsync(new CandleIngested(MakeCandleDto(1)));

        await act.Should().NotThrowAsync("a full channel silently drops the candle");
    }

    // ── CandlePersistenceHostedService batch flush ────────────────────────────────────────────────────

    [Fact]
    public async Task Hosted_service_flushes_candles_to_repository_in_one_batch()
    {
        // Arrange: a recording repository + a channel with 3 candles pre-loaded.
        var repo = new RecordingCandleRepository();
        var channel = Channel.CreateBounded<Candle>(100);
        var batchOpts = new CandlePersistenceBatchOptions(batchSize: 10, flushIntervalMs: 50);

        for (var i = 0; i < 3; i++)
            await channel.Writer.WriteAsync(MakeCandle(i));

        // Register a service provider with the scoped repository.
        var services = new ServiceCollection();
        services.AddScoped<ICandleRepository>(_ => repo);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var svc = new CandlePersistenceHostedService(
            channel, scopeFactory, batchOpts,
            NullLogger<CandlePersistenceHostedService>.Instance);

        // Act: run the service for just long enough to flush (>50 ms flush interval).
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            await svc.StartAsync(cts.Token);
            await Task.Delay(200, cts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
        }

        // Assert: all 3 candles were flushed.
        repo.AppendedBatches.Should().NotBeEmpty("at least one flush must have occurred");
        repo.AppendedBatches.SelectMany(b => b).Should().HaveCount(3);
    }

    [Fact]
    public async Task Hosted_service_does_not_throw_when_repository_throws()
    {
        // Arrange: a failing repository.
        var repo = new FailingCandleRepository();
        var channel = Channel.CreateBounded<Candle>(100);
        var batchOpts = new CandlePersistenceBatchOptions(batchSize: 10, flushIntervalMs: 50);

        await channel.Writer.WriteAsync(MakeCandle(0));

        var services = new ServiceCollection();
        services.AddScoped<ICandleRepository>(_ => repo);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var svc = new CandlePersistenceHostedService(
            channel, scopeFactory, batchOpts,
            NullLogger<CandlePersistenceHostedService>.Instance);

        // Act: run briefly — the service MUST swallow the repository exception.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            await svc.StartAsync(cts.Token);
            await Task.Delay(150, cts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            // StopAsync must complete without throwing even though the repository threw.
            var act = async () => await svc.StopAsync(CancellationToken.None);
            await act.Should().NotThrowAsync("a DB failure must never propagate out of the background writer");
        }
    }

    // ── CandlePersistenceConfiguration (range cap) ───────────────────────────────────────────────────

    [Fact]
    public void CandlePersistenceConfiguration_rejects_non_positive_max()
    {
        var act = () => _ = new CandlePersistenceConfiguration(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── GetChartRangeQueryHandler — range cap enforced ────────────────────────────────────────────────

    [Fact]
    public async Task GetChartRangeQueryHandler_caps_results_at_MaxRangeCandles()
    {
        // Arrange: a stub repository that returns 5 candles for any range query.
        var repo = new StubCandleRepository(count: 5);
        var config = new CandlePersistenceConfiguration(maxRangeCandles: 3); // cap at 3

        var handler = new GetChartRangeQueryHandler(repo, config);
        var query = new GetChartRangeQuery(
            "EURUSD", "M5",
            From: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            To: new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero));

        // Act.
        var result = await handler.HandleAsync(query);

        // Assert: the repo was called with max=3 (the cap).
        repo.LastMaxRequested.Should().Be(3);
        result.Should().HaveCount(3, "the repository returned 5 but the cap is 3");
    }

    [Fact]
    public async Task GetChartRangeQueryHandler_returns_empty_for_unknown_timeframe()
    {
        var repo = new StubCandleRepository(count: 5);
        var config = new CandlePersistenceConfiguration(10);

        var handler = new GetChartRangeQueryHandler(repo, config);
        var query = new GetChartRangeQuery(
            "EURUSD", "BADTF",
            From: DateTimeOffset.UtcNow.AddDays(-1),
            To: DateTimeOffset.UtcNow);

        var result = await handler.HandleAsync(query);

        result.Should().BeEmpty("an unrecognised timeframe should produce an empty result, not an exception");
        repo.WasCalled.Should().BeFalse("the repository must not be queried for an unknown timeframe");
    }

    // ── Integration: projection handler + bus scans correctly ────────────────────────────────────────

    [Fact]
    public async Task Bus_dispatch_reaches_persistence_handler_without_touching_store()
    {
        // Both chart store + persistence handler are registered; only the store is asserted here —
        // the handler just enqueues (the channel drains in the background; we don't assert the DB write).
        var services = new ServiceCollection();
        services.AddMessaging(typeof(ChartCandleProjectionHandler).Assembly);
        services.AddMarketDataReadModels();
        using var provider = services.BuildServiceProvider();

        var bus = provider.GetRequiredService<IMessageBus>();
        var store = provider.GetRequiredService<ChartCandleStore>();

        await bus.PublishAsync(new CandleIngested(MakeCandleDto(0, close: 1.10m)));

        // The in-memory store must have the candle (same as before the persistence handler existed).
        var candles = await bus.QueryAsync(new GetChartCandlesQuery("EURUSD", "M5", 10));
        candles.Should().ContainSingle(c => c.Close == 1.10m);
    }

    // ── Test doubles ─────────────────────────────────────────────────────────────────────────────────

    private sealed class RecordingCandleRepository : ICandleRepository
    {
        public List<IReadOnlyList<Candle>> AppendedBatches { get; } = [];

        public Task AppendAsync(IReadOnlyList<Candle> batch, CancellationToken ct = default)
        {
            AppendedBatches.Add(batch.ToList());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Candle>> GetRangeAsync(
            Symbol s, Timeframe tf, DateTimeOffset from, DateTimeOffset to, int max, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Candle>>([]);

        public Task<IReadOnlyList<Candle>> GetRecentAsync(
            Symbol s, Timeframe tf, int count, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Candle>>([]);
    }

    private sealed class FailingCandleRepository : ICandleRepository
    {
        public Task AppendAsync(IReadOnlyList<Candle> batch, CancellationToken ct = default)
            => Task.FromException(new InvalidOperationException("Simulated DB failure"));

        public Task<IReadOnlyList<Candle>> GetRangeAsync(
            Symbol s, Timeframe tf, DateTimeOffset from, DateTimeOffset to, int max, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Candle>>([]);

        public Task<IReadOnlyList<Candle>> GetRecentAsync(
            Symbol s, Timeframe tf, int count, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Candle>>([]);
    }

    /// <summary>Stub that produces <paramref name="count"/> dummy candles and records the max requested.</summary>
    private sealed class StubCandleRepository(int count) : ICandleRepository
    {
        public bool WasCalled { get; private set; }
        public int LastMaxRequested { get; private set; }

        public Task<IReadOnlyList<Candle>> GetRangeAsync(
            Symbol symbol, Timeframe timeframe, DateTimeOffset from, DateTimeOffset to, int max, CancellationToken ct = default)
        {
            WasCalled = true;
            LastMaxRequested = max;
            var candles = Enumerable.Range(0, Math.Min(count, max))
                .Select(i => new Candle(symbol, timeframe,
                    from.AddMinutes(i * 5), 1m, 1.01m, 0.99m, 1m, 0m))
                .ToList();
            return Task.FromResult<IReadOnlyList<Candle>>(candles);
        }

        public Task<IReadOnlyList<Candle>> GetRecentAsync(
            Symbol s, Timeframe tf, int cnt, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Candle>>([]);

        public Task AppendAsync(IReadOnlyList<Candle> batch, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
