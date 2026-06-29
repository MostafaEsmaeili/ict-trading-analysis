using System.Threading.Channels;
using DotNet.Testcontainers.Builders;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Application.Chart;
using IctTrader.MarketData.Application.Persistence;
using IctTrader.MarketData.Contracts;
using IctTrader.MarketData.Infrastructure.Persistence;
using IctTrader.MarketData.Infrastructure.Persistence.Repositories;
using IctTrader.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Round-trip integration tests for <see cref="MarketDataDbContext"/> against a real Postgres instance
/// (plan §7 / §8.1).  Mirrors the <see cref="PaperTradingPersistenceTests"/> fixture pattern: one container
/// per test class, EF migrations applied once, Respawn resets between tests.
/// </summary>
/// <remarks>
/// These tests require Docker to be running (the same <see cref="DockerRequiredFactAttribute"/> guard is
/// used). They are in a separate Xunit collection so their container lifecycle is isolated from the
/// PaperTrading fixture.
/// NOTE: these tests COMPILE correctly and are architecturally sound but require a running Docker daemon
/// to execute. In the development sandbox (where Docker is not reachable) they are automatically
/// SKIPPED by <see cref="DockerRequiredFactAttribute"/> — run them with Docker available.
/// </remarks>
[Collection("MarketDataDb")]
public sealed class MarketDataPersistenceTests : IAsyncLifetime
{
    private readonly MarketDataDbFixture _fixture;

    public MarketDataPersistenceTests(MarketDataDbFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private MarketDataDbContext CreateContext() => _fixture.CreateContext();

    private ICandleRepository CreateRepository() =>
        new CandleRepository(_fixture.CreateContext());

    private static readonly Symbol EurUsd = new("EURUSD");
    private static readonly DateTimeOffset Epoch = new(2024, 3, 4, 9, 0, 0, TimeSpan.Zero);

    private static Candle MakeCandle(int barIndex, string symbol = "EURUSD", string tf = "M5") =>
        new(new Symbol(symbol),
            Enum.Parse<Timeframe>(tf),
            Epoch.AddMinutes(barIndex * 5),
            1.09m + barIndex * 0.001m,
            1.11m + barIndex * 0.001m,
            1.08m + barIndex * 0.001m,
            1.10m + barIndex * 0.001m,
            1000m + barIndex);

    // ── UPSERT idempotency ────────────────────────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task AppendAsync_inserts_new_candles()
    {
        // Arrange: two distinct candles.
        var repo = CreateRepository();
        var batch = new[] { MakeCandle(0), MakeCandle(1) };

        // Act.
        await repo.AppendAsync(batch);

        // Assert: both are in the DB.
        await using var ctx = CreateContext();
        var count = await ctx.Candles.CountAsync(c => c.Symbol == "EURUSD");
        count.Should().Be(2);
    }

    [DockerRequiredFact]
    public async Task AppendAsync_is_idempotent_same_candle_twice_yields_one_row()
    {
        // Arrange: one candle, appended twice.
        var repo = CreateRepository();
        var candle = MakeCandle(0);

        // Act.
        await repo.AppendAsync([candle]);
        await repo.AppendAsync([candle]);   // second write on the same natural key → no-op

        // Assert: exactly ONE row.
        await using var ctx = CreateContext();
        var count = await ctx.Candles.CountAsync(c => c.Symbol == "EURUSD");
        count.Should().Be(1, "INSERT ON CONFLICT DO NOTHING must deduplicate");
    }

    [DockerRequiredFact]
    public async Task AppendAsync_is_idempotent_same_candle_in_same_batch_yields_one_row()
    {
        // A batch with two IDENTICAL entries — the second ON CONFLICT DO NOTHING.
        var repo = CreateRepository();
        var candle = MakeCandle(0);

        await repo.AppendAsync([candle, candle]);

        await using var ctx = CreateContext();
        var count = await ctx.Candles.CountAsync(c => c.Symbol == "EURUSD");
        count.Should().Be(1, "duplicate rows in the same batch are also deduplicated");
    }

    // ── GetRecentAsync ────────────────────────────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task GetRecentAsync_returns_N_most_recent_candles_in_chronological_order()
    {
        // Arrange: 5 candles persisted.
        var repo = CreateRepository();
        var batch = Enumerable.Range(0, 5).Select(i => MakeCandle(i)).ToList();
        await repo.AppendAsync(batch);

        // Act: request the 3 most-recent.
        var result = await repo.GetRecentAsync(EurUsd, Timeframe.M5, 3);

        // Assert: 3 candles, oldest→newest.
        result.Should().HaveCount(3);
        result[0].OpenTimeUtc.Should().Be(Epoch.AddMinutes(2 * 5), "third bar (index 2) is oldest in the 3");
        result[2].OpenTimeUtc.Should().Be(Epoch.AddMinutes(4 * 5), "fifth bar (index 4) is newest");
    }

    [DockerRequiredFact]
    public async Task GetRecentAsync_returns_empty_for_unknown_symbol()
    {
        var repo = CreateRepository();
        await repo.AppendAsync([MakeCandle(0)]);

        var result = await repo.GetRecentAsync(new Symbol("NOPE"), Timeframe.M5, 10);

        result.Should().BeEmpty();
    }

    // ── GetRangeAsync ─────────────────────────────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task GetRangeAsync_returns_candles_in_window_chronologically()
    {
        // Arrange: 10 candles, bars 0–9.
        var repo = CreateRepository();
        var batch = Enumerable.Range(0, 10).Select(i => MakeCandle(i)).ToList();
        await repo.AppendAsync(batch);

        // Query for bars 2–5 (inclusive by open time).
        var from = Epoch.AddMinutes(2 * 5);
        var to = Epoch.AddMinutes(5 * 5);

        var result = await repo.GetRangeAsync(EurUsd, Timeframe.M5, from, to, max: 100);

        result.Should().HaveCount(4, "bars 2, 3, 4, 5");
        result.First().OpenTimeUtc.Should().Be(from);
        result.Last().OpenTimeUtc.Should().Be(to);
    }

    [DockerRequiredFact]
    public async Task GetRangeAsync_respects_max_cap()
    {
        // Arrange: 10 candles.
        var repo = CreateRepository();
        var batch = Enumerable.Range(0, 10).Select(i => MakeCandle(i)).ToList();
        await repo.AppendAsync(batch);

        // Query the whole range but cap at 3.
        var result = await repo.GetRangeAsync(
            EurUsd, Timeframe.M5, Epoch, Epoch.AddMinutes(50), max: 3);

        result.Should().HaveCount(3, "the max cap must be honoured");
    }

    [DockerRequiredFact]
    public async Task GetRangeAsync_returns_empty_when_no_rows_in_window()
    {
        var repo = CreateRepository();
        await repo.AppendAsync([MakeCandle(0)]);

        // Query far in the future.
        var result = await repo.GetRangeAsync(
            EurUsd, Timeframe.M5,
            Epoch.AddYears(10), Epoch.AddYears(11), max: 100);

        result.Should().BeEmpty();
    }

    // ── Domain VO round-trip ──────────────────────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task Candle_OHLC_and_timestamp_round_trip_without_precision_loss()
    {
        var repo = CreateRepository();
        var original = new Candle(
            EurUsd, Timeframe.M15,
            new DateTimeOffset(2024, 6, 15, 13, 45, 0, TimeSpan.Zero),
            1.08765m, 1.09001m, 1.08500m, 1.08888m, 12345.678m);

        await repo.AppendAsync([original]);

        var loaded = (await repo.GetRecentAsync(EurUsd, Timeframe.M15, 1)).Single();

        loaded.Symbol.Value.Should().Be("EURUSD");
        loaded.Timeframe.Should().Be(Timeframe.M15);
        loaded.OpenTimeUtc.Should().Be(original.OpenTimeUtc);
        loaded.Open.Should().Be(1.08765m);
        loaded.High.Should().Be(1.09001m);
        loaded.Low.Should().Be(1.08500m);
        loaded.Close.Should().Be(1.08888m);
        loaded.Volume.Should().Be(12345.678m);
    }

    // ── End-to-end: projection handler → channel → hosted service → repository ─────────────────────

    [DockerRequiredFact]
    public async Task End_to_end_pipeline_persists_candles_from_CandleIngested_via_bus()
    {
        // Arrange: set up the full pipeline against the real DB.
        var channel = Channel.CreateBounded<Candle>(100);
        var batchOpts = new CandlePersistenceBatchOptions(batchSize: 10, flushIntervalMs: 50);

        var services = new ServiceCollection();
        services.AddScoped(_ => CreateRepository());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var projectionHandler = new CandlePersistenceProjectionHandler(
            channel, NullLogger<CandlePersistenceProjectionHandler>.Instance);

        var hostedService = new CandlePersistenceHostedService(
            channel, scopeFactory, batchOpts,
            NullLogger<CandlePersistenceHostedService>.Instance);

        using var cts = new CancellationTokenSource();

        // Start the background writer.
        await hostedService.StartAsync(cts.Token);

        // Act: publish two CandleIngested events through the projection handler.
        var dto0 = new CandleDto("EURUSD", "M5", Epoch, 1.09m, 1.11m, 1.08m, 1.10m, 1000m);
        var dto1 = new CandleDto("EURUSD", "M5", Epoch.AddMinutes(5), 1.10m, 1.12m, 1.09m, 1.11m, 900m);

        await projectionHandler.HandleAsync(new CandleIngested(dto0));
        await projectionHandler.HandleAsync(new CandleIngested(dto1));

        // Wait for the flush interval to pass + a margin.
        await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);

        // Stop gracefully (drains the channel on shutdown).
        cts.Cancel();
        await hostedService.StopAsync(CancellationToken.None);

        // Assert: both candles are in the DB via the repository.
        var result = await CreateRepository()
            .GetRecentAsync(EurUsd, Timeframe.M5, 10);

        result.Should().HaveCount(2);
        result[0].Open.Should().Be(1.09m);
        result[1].Open.Should().Be(1.10m);
    }
}

// ── Shared fixture ────────────────────────────────────────────────────────────────────────────────────

[CollectionDefinition("MarketDataDb")]
public sealed class MarketDataDbCollection : ICollectionFixture<MarketDataDbFixture>
{
}

/// <summary>
/// Boots a Testcontainers Postgres instance ONCE for the MarketData persistence tests, applies the
/// <see cref="MarketDataDbContext"/> EF migrations, and exposes a Respawn-based reset so each test starts
/// with a clean <c>candles</c> table.
/// </summary>
public sealed class MarketDataDbFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;

    public string ConnectionString => _container!.GetConnectionString();

    public MarketDataDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MarketDataDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new MarketDataDbContext(options);
    }

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("ict_md_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _container.StartAsync();

        // Apply the MarketData EF migrations (creates the candles table).
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
        });
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await _respawner!.ResetAsync(conn);
    }
}
