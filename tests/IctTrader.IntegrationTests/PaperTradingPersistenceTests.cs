using DotNet.Testcontainers.Builders;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.PaperTrading.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Round-trip integration tests for <see cref="PaperTradingDbContext"/> against a real Postgres instance
/// (plan §8.1).  The tests use Testcontainers to boot an isolated Postgres container per test class,
/// apply the EF migrations, and use Respawn to truncate tables between tests so each test starts clean.
/// </summary>
/// <remarks>
/// These tests require Docker to be running.  They are collected in a class fixture so the container
/// starts ONCE and migrations run ONCE; Respawn resets data between each test.  If Docker is unavailable
/// the tests are skipped via the <see cref="DockerRequiredFact"/> attribute.
/// </remarks>
[Collection("PaperTradingDb")]
public sealed class PaperTradingPersistenceTests : IAsyncLifetime
{
    private readonly PaperTradingDbFixture _fixture;

    public PaperTradingPersistenceTests(PaperTradingDbFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private PaperTradingDbContext CreateContext() => _fixture.CreateContext();

    private static readonly DateTimeOffset Epoch = new(2024, 3, 4, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Builds a minimal valid bullish setup snapshot for the test trade.</summary>
    private static Setup BuildSetup()
    {
        var direction = Direction.Bullish;
        var plan = new TradePlan(
            direction,
            entry: new Price(1.0900m),
            stop: new Price(1.0850m),
            targets: new TargetLadder(direction, new Price(1.0950m), new Price(1.1000m)));

        return new Setup(
            symbol: new Symbol("EURUSD"),
            style: TradeStyle.Intraday,
            timeframe: Timeframe.M5,
            grade: SetupGrade.A,
            score: 85,
            plan: plan,
            reason: new SetupReason("Bullish FVG inside London killzone after Asian high sweep; MSS confirmed"),
            confirmedAtUtc: Epoch);
    }

    /// <summary>Opens a seeded account in a fresh context and returns it.</summary>
    private async Task<PaperAccount> SeedAccountAsync(Guid accountId, decimal equity = 10_000m)
    {
        await using var ctx = CreateContext();
        var account = new PaperAccount(accountId, new Money(equity), maxOpenPortfolioRiskPercent: 5m);
        ctx.PaperAccounts.Add(account);
        await ctx.SaveChangesAsync();
        return account;
    }

    // ── PaperAccount round-trip ───────────────────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task PaperAccount_persists_and_reloads_equity_and_reservation_ledger()
    {
        // Arrange: create an account with two reservations.
        var accountId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await using (var ctx = CreateContext())
        {
            var account = new PaperAccount(accountId, new Money(10_000m), 5m);
            account.Reserve(id1, new Money(100m));
            account.Reserve(id2, new Money(200m));
            ctx.PaperAccounts.Add(account);
            await ctx.SaveChangesAsync();
        }

        // Act: reload in a fresh context.
        PaperAccount loaded;
        await using (var ctx = CreateContext())
        {
            loaded = await ctx.PaperAccounts.SingleAsync(a => a.Id == accountId);
        }

        // Assert: structural properties match.
        loaded.Id.Should().Be(accountId);
        loaded.Equity.Amount.Should().Be(10_000m);
        loaded.MaxOpenPortfolioRiskPercent.Should().Be(5m);

        // The reservation ledger is loaded: OpenRisk == 300.
        loaded.OpenRisk.Amount.Should().Be(300m);

        // The cap is still enforced on the reloaded aggregate: a 9701+ reservation would breach it.
        loaded.CanOpen(new Money(200m)).Should().BeTrue("200 fits within the 500-cap");
        loaded.CanOpen(new Money(400m)).Should().BeFalse("400 + 300 = 700 > 500 cap");
    }

    // ── PaperTrade round-trip (open only) ────────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task PaperTrade_persists_and_reloads_open_trade()
    {
        // Arrange.
        var accountId = Guid.NewGuid();
        await SeedAccountAsync(accountId);

        var tradeId = Guid.NewGuid();
        var setup = BuildSetup();

        await using (var ctx = CreateContext())
        {
            var account = await ctx.PaperAccounts.SingleAsync(a => a.Id == accountId);
            var trade = new PaperTrade(
                tradeId, accountId,
                symbol: new Symbol("EURUSD"),
                style: TradeStyle.Intraday,
                timeframe: Timeframe.M5,
                plan: setup.Plan,
                size: new PositionSize(0.1m),
                pipSize: 0.0001m,
                valuePerPip: 1m,
                openedAtUtc: Epoch);

            account.RegisterOpen(trade);
            ctx.PaperTrades.Add(trade);
            await ctx.SaveChangesAsync();
        }

        // Act: reload.
        PaperTrade loaded;
        await using (var ctx = CreateContext())
        {
            loaded = await ctx.PaperTrades.SingleAsync(t => t.Id == tradeId);
        }

        // Assert.
        loaded.Id.Should().Be(tradeId);
        loaded.AccountId.Should().Be(accountId);
        loaded.Symbol.Value.Should().Be("EURUSD");
        loaded.Style.Should().Be(TradeStyle.Intraday);
        loaded.Timeframe.Should().Be(Timeframe.M5);
        loaded.Status.Should().Be(TradeStatus.Open);
        loaded.Lifecycle.Should().Be(TradeLifecycle.Open);
        loaded.HasScaledOut.Should().BeFalse();
        loaded.OpenedAtUtc.Should().Be(Epoch);
        loaded.Plan.Direction.Should().Be(Direction.Bullish);
        loaded.Plan.Entry.Value.Should().Be(1.0900m);
        loaded.Plan.Stop.Value.Should().Be(1.0850m);
        loaded.Plan.Targets.Partial.Value.Should().Be(1.0950m);
        loaded.Plan.Targets.Runner.Value.Should().Be(1.1000m);
        loaded.Size.Lots.Should().Be(0.1m);
        loaded.RemainingSize.Lots.Should().Be(0.1m);
        loaded.CurrentStop.Value.Should().Be(1.0850m);
        // RiskBudget = initialRiskPerUnit / pipSize * valuePerPipForPosition
        //            = 0.005 / 0.0001 * (1 * 0.1)   [valuePerPipForPosition = valuePerPip * size.Lots]
        //            = 50 * 0.1 = 5.0
        loaded.RiskBudget.Amount.Should().BeApproximately(5m, 0.01m, "0.005/0.0001 * (1*0.1) = 50 * 0.1 = 5.0");
        loaded.InitialRiskPerUnit.Should().Be(0.005m);
        loaded.RealizedR.Should().BeNull();
        loaded.GrossPnl.Should().BeNull();
        loaded.Legs.Should().BeEmpty();
    }

    // ── PaperTrade full lifecycle: scale-out → close → reload ────────────────────────────────────────

    [DockerRequiredFact]
    public async Task PaperTrade_scale_out_then_close_round_trips_derived_figures()
    {
        // Arrange: open the trade.
        var accountId = Guid.NewGuid();
        await SeedAccountAsync(accountId);

        var tradeId = Guid.NewGuid();
        var setup = BuildSetup();
        var t1 = Epoch.AddMinutes(30);
        var t2 = Epoch.AddMinutes(60);

        await using (var ctx = CreateContext())
        {
            var account = await ctx.PaperAccounts.SingleAsync(a => a.Id == accountId);
            var trade = new PaperTrade(
                tradeId, accountId,
                symbol: new Symbol("EURUSD"),
                style: TradeStyle.Intraday,
                timeframe: Timeframe.M5,
                plan: setup.Plan,
                size: new PositionSize(0.2m),
                pipSize: 0.0001m,
                valuePerPip: 1m,
                openedAtUtc: Epoch);

            account.RegisterOpen(trade);

            // Scale out half at T1 (+50 pips = +1R on that leg).
            var partialCosts = new TradeCosts(new Money(0.14m), new Money(3m));
            trade.ScaleOut(
                new Price(1.0950m),
                new PositionSize(0.1m),
                partialCosts,
                TradeCloseReason.TargetHit,
                t1);

            ctx.PaperTrades.Add(trade);
            await ctx.SaveChangesAsync();
        }

        // Act-1: close the runner in a second context.
        await using (var ctx = CreateContext())
        {
            var trade = await ctx.PaperTrades.SingleAsync(t => t.Id == tradeId);
            var runnerCosts = new TradeCosts(new Money(0.14m), new Money(3m));
            trade.Close(new Price(1.1000m), TradeCloseReason.TargetHit, runnerCosts, t2);
            await ctx.SaveChangesAsync();
        }

        // Act-2: reload in a third context.
        PaperTrade loaded;
        await using (var ctx = CreateContext())
        {
            loaded = await ctx.PaperTrades.SingleAsync(t => t.Id == tradeId);
        }

        // Assert: lifecycle.
        loaded.Status.Should().Be(TradeStatus.Closed);
        loaded.Lifecycle.Should().Be(TradeLifecycle.Closed);
        loaded.HasScaledOut.Should().BeTrue();
        loaded.CloseReason.Should().Be(TradeCloseReason.TargetHit);
        loaded.ClosedAtUtc.Should().Be(t2);
        loaded.ExitPrice!.Value.Value.Should().Be(1.1000m);

        // Fill legs: two legs (scale-out + runner).
        loaded.Legs.Should().HaveCount(2, "one partial + one final leg");
        loaded.Legs[0].ExitPrice.Value.Should().Be(1.0950m);
        loaded.Legs[0].Lots.Lots.Should().Be(0.1m);
        loaded.Legs[1].ExitPrice.Value.Should().Be(1.1000m);
        loaded.Legs[1].Lots.Lots.Should().Be(0.1m);

        // Derived figures:
        // Partial leg: (1.0950 - 1.0900) / 0.0001 = 50 pips, 0.1 lots × 1 VPP = $5.00 gross
        // Runner leg:  (1.1000 - 1.0900) / 0.0001 = 100 pips, 0.1 lots × 1 VPP = $10.00 gross
        // Total gross = $15.00; total costs = $6.28 ($3.14 × 2)
        const decimal expectedGross = 15m;
        const decimal expectedCosts = 6.28m;
        const decimal expectedNet = expectedGross - expectedCosts;

        loaded.GrossPnl!.Value.Amount.Should().BeApproximately(expectedGross, 0.001m);
        loaded.Costs!.Value.Amount.Should().BeApproximately(expectedCosts, 0.001m);
        loaded.RealizedPnl!.Value.Amount.Should().BeApproximately(expectedNet, 0.001m);

        // RealizedR = gross / riskBudget; riskBudget = (0.005 / 0.0001) * 0.2 * 1 = $100
        // Gross $15 / $100 = 0.15R? No — this trade has size 0.2 lots, pipSize 0.0001, VPP 1.
        // RiskBudget = |entry - stop| / pipSize * VPP * lots = 0.005/0.0001 * 1 * 0.2 = 10.0
        // Partial: 5pips * 0.1lots * (1/0.0001) = wait, let me recalculate.
        // LegGross = signedMove / pipSize * valuePerPip * legLots
        //          = (1.0950-1.0900) / 0.0001 * 1 * 0.1 = 0.005/0.0001 * 0.1 = 50 * 0.1 = 5.0
        // Runner: (1.1000-1.0900)/0.0001 * 1 * 0.1 = 100 * 0.1 = 10.0
        // GrossPnl = 15.0; RiskBudget = 0.005/0.0001 * 1 * 0.2 = 50*0.2=10.0
        // Wait: ValuePerPipForPosition = valuePerPip * size.Lots = 1 * 0.2 = 0.2
        // RiskBudget = initialRiskPerUnit / pipSize * valuePerPipForPosition
        //            = 0.005 / 0.0001 * 0.2 = 50 * 0.2 = 10.0
        // LegGross for partial: 0.005/0.0001 * 1 * 0.1 = 5.0
        // LegGross for runner:  0.01/0.0001  * 1 * 0.1 = 10.0
        // GrossPnl = 15.0; RealizedR = 15 / 10 = 1.5
        loaded.RiskBudget.Amount.Should().BeApproximately(10m, 0.001m);
        loaded.RealizedR.Should().BeApproximately(1.5m, 0.001m, "size-weighted blend: 0.5@+1R + 0.5@+2R = 1.5R");
        loaded.NetR.Should().BeApproximately(expectedNet / 10m, 0.001m);
    }

    // ── ArmedEntry round-trip ─────────────────────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task ArmedEntry_persists_and_reloads_with_setup_snapshot()
    {
        // Arrange.
        var entryId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var setup = BuildSetup();

        await using (var ctx = CreateContext())
        {
            var entry = new ArmedEntry(
                entryId, accountId, setup,
                size: new PositionSize(0.1m),
                riskBudget: new Money(50m),
                pipSize: 0.0001m,
                valuePerPip: 1m,
                instrumentClass: InstrumentClass.Fx,
                armedAtUtc: Epoch);
            ctx.ArmedEntries.Add(entry);
            await ctx.SaveChangesAsync();
        }

        // Act: reload.
        ArmedEntry loaded;
        await using (var ctx = CreateContext())
        {
            loaded = await ctx.ArmedEntries.SingleAsync(e => e.Id == entryId);
        }

        // Assert.
        loaded.Id.Should().Be(entryId);
        loaded.AccountId.Should().Be(accountId);
        loaded.Status.Should().Be(ArmedEntryStatus.Armed);
        loaded.ArmedAtUtc.Should().Be(Epoch);
        loaded.Size.Lots.Should().Be(0.1m);
        loaded.RiskBudget.Amount.Should().Be(50m);
        loaded.PipSize.Should().Be(0.0001m);
        loaded.ValuePerPip.Should().Be(1m);
        loaded.InstrumentClass.Should().Be(InstrumentClass.Fx);

        // Setup snapshot round-trip.
        loaded.Setup.Symbol.Value.Should().Be("EURUSD");
        loaded.Setup.Style.Should().Be(TradeStyle.Intraday);
        loaded.Setup.Grade.Should().Be(SetupGrade.A);
        loaded.Setup.Score.Should().Be(85);
        loaded.Setup.Plan.Entry.Value.Should().Be(1.0900m);
        loaded.Setup.Plan.Stop.Value.Should().Be(1.0850m);
        loaded.Setup.Plan.Targets.Partial.Value.Should().Be(1.0950m);
        loaded.Setup.Plan.Targets.Runner.Value.Should().Be(1.1000m);
        loaded.Setup.Reason.Text.Should().Contain("Bullish FVG");
        loaded.Setup.ConfirmedAtUtc.Should().Be(Epoch);

        // Derived delegation (from Setup).
        loaded.Symbol.Value.Should().Be("EURUSD");
        loaded.Direction.Should().Be(Direction.Bullish);
    }

    // ── Cancel an armed entry ─────────────────────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task ArmedEntry_cancel_persists_cancelled_status()
    {
        var entryId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var setup = BuildSetup();

        await using (var ctx = CreateContext())
        {
            var entry = new ArmedEntry(
                entryId, accountId, setup,
                new PositionSize(0.1m), new Money(50m),
                0.0001m, 1m, InstrumentClass.Fx, Epoch);
            ctx.ArmedEntries.Add(entry);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var entry = await ctx.ArmedEntries.SingleAsync(e => e.Id == entryId);
            entry.Cancel(EntryCancelReason.KillzoneEnded, Epoch.AddMinutes(30));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var entry = await ctx.ArmedEntries.SingleAsync(e => e.Id == entryId);
            entry.Status.Should().Be(ArmedEntryStatus.Cancelled);
        }
    }
}

// ── Shared fixture ────────────────────────────────────────────────────────────────────────────────────

[CollectionDefinition("PaperTradingDb")]
public sealed class PaperTradingDbCollection : ICollectionFixture<PaperTradingDbFixture>
{
}

/// <summary>
/// Boots a Testcontainers Postgres instance ONCE per test run, applies EF migrations, and exposes a
/// Respawn-based reset so each test starts with a clean slate (plan §8.1/§8.2).
/// </summary>
public sealed class PaperTradingDbFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;

    private string ConnectionString => _container!.GetConnectionString();

    public PaperTradingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperTradingDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new PaperTradingDbContext(options);
    }

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("ict_pt_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _container.StartAsync();

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

/// <summary>
/// Fact attribute that skips when Docker is unavailable, so CI without Docker doesn't red.
/// The test is compiled and type-checks correctly in all environments — only execution is conditional.
/// </summary>
public sealed class DockerRequiredFactAttribute : FactAttribute
{
    public DockerRequiredFactAttribute()
    {
        if (!IsDockerAvailable())
            Skip = "Docker is not available in this environment; test requires Testcontainers Postgres.";
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            // Testcontainers probes Docker on first use; we do a lightweight pre-flight here.
            var socket = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? "/var/run/docker.sock";
            return Environment.GetEnvironmentVariable("SKIP_DOCKER_TESTS") != "true"
                && (socket.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase)
                    || File.Exists(socket)
                    || OperatingSystem.IsWindows());
        }
        catch
        {
            return false;
        }
    }
}
