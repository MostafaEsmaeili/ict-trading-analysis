using IctTrader.Domain.Repositories;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.PaperTrading.Infrastructure.Persistence;
using IctTrader.PaperTrading.Infrastructure.Persistence.Repositories;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Round-trip integration tests for the WP7 slice 2d-ii PaperTrading aggregate repositories and
/// unit-of-work against a real Postgres instance (plan §7/§8.1). Shares the
/// <see cref="PaperTradingDbFixture"/> Testcontainers container (boot once, Respawn between tests).
/// </summary>
[Collection("PaperTradingDb")]
public sealed class PaperTradingRepositoryTests : IAsyncLifetime
{
    private readonly PaperTradingDbFixture _fixture;

    public PaperTradingRepositoryTests(PaperTradingDbFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Epoch = new(2024, 3, 4, 9, 30, 0, TimeSpan.Zero);

    private PaperTradingDbContext CreateContext() => _fixture.CreateContext();

    private static IPaperAccountRepository AccountRepo(PaperTradingDbContext ctx) =>
        new PaperAccountRepository(ctx);

    private static IPaperTradeRepository TradeRepo(PaperTradingDbContext ctx) =>
        new PaperTradeRepository(ctx);

    private static IArmedEntryRepository EntryRepo(PaperTradingDbContext ctx) =>
        new ArmedEntryRepository(ctx);

    private static IPaperTradingUnitOfWork Uow(PaperTradingDbContext ctx) =>
        new PaperTradingUnitOfWork(ctx);

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
            reason: new SetupReason("Bullish FVG after Asian high sweep; MSS confirmed"),
            confirmedAtUtc: Epoch);
    }

    // ── IPaperAccountRepository round-trip ────────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task PaperAccountRepository_add_and_reload_preserves_equity_and_ledger()
    {
        var accountId = Guid.NewGuid();
        var rid1 = Guid.NewGuid();
        var rid2 = Guid.NewGuid();

        // Add via repo + UoW.
        await using (var ctx = CreateContext())
        {
            var account = new PaperAccount(accountId, new Money(20_000m), maxOpenPortfolioRiskPercent: 5m);
            account.Reserve(rid1, new Money(150m));
            account.Reserve(rid2, new Money(250m));
            await AccountRepo(ctx).AddAsync(account);
            await Uow(ctx).SaveChangesAsync();
        }

        // Reload in a fresh context.
        PaperAccount loaded;
        await using (var ctx = CreateContext())
        {
            loaded = (await AccountRepo(ctx).GetByIdAsync(accountId))!;
        }

        loaded.Should().NotBeNull();
        loaded.Id.Should().Be(accountId);
        loaded.Equity.Amount.Should().Be(20_000m);
        loaded.MaxOpenPortfolioRiskPercent.Should().Be(5m);
        // Reservation ledger survived: OpenRisk == 400, cap == 20_000*0.05 == 1000 → 200 fits, 700 does not.
        loaded.OpenRisk.Amount.Should().Be(400m);
        loaded.CanOpen(new Money(200m)).Should().BeTrue("200 + 400 = 600 ≤ 1000 cap");
        loaded.CanOpen(new Money(700m)).Should().BeFalse("700 + 400 = 1100 > 1000 cap");
    }

    // ── IPaperTradeRepository: add + reload ───────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task PaperTradeRepository_add_and_reload_open_trade_preserves_all_fields()
    {
        // Seed the owning account (FK constraint).
        var accountId = Guid.NewGuid();
        await using (var ctx = CreateContext())
        {
            await AccountRepo(ctx).AddAsync(new PaperAccount(accountId, new Money(10_000m), 5m));
            await Uow(ctx).SaveChangesAsync();
        }

        var tradeId = Guid.NewGuid();
        var plan = BuildSetup().Plan;

        await using (var ctx = CreateContext())
        {
            var account = (await AccountRepo(ctx).GetByIdAsync(accountId))!;
            var trade = new PaperTrade(
                tradeId, accountId, new Symbol("EURUSD"),
                TradeStyle.Intraday, Timeframe.M5, plan,
                new PositionSize(0.1m), 0.0001m, 1m, Epoch);
            account.RegisterOpen(trade);
            await TradeRepo(ctx).AddAsync(trade);
            await Uow(ctx).SaveChangesAsync();
        }

        PaperTrade loaded;
        await using (var ctx = CreateContext())
        {
            loaded = (await TradeRepo(ctx).GetByIdAsync(tradeId))!;
        }

        loaded.Should().NotBeNull();
        loaded.Id.Should().Be(tradeId);
        loaded.AccountId.Should().Be(accountId);
        loaded.Symbol.Value.Should().Be("EURUSD");
        loaded.Style.Should().Be(TradeStyle.Intraday);
        loaded.Timeframe.Should().Be(Timeframe.M5);
        loaded.Status.Should().Be(TradeStatus.Open);
        loaded.Lifecycle.Should().Be(TradeLifecycle.Open);
        loaded.Plan.Entry.Value.Should().Be(1.0900m);
        loaded.Plan.Stop.Value.Should().Be(1.0850m);
        loaded.Size.Lots.Should().Be(0.1m);
        loaded.RemainingSize.Lots.Should().Be(0.1m);
        loaded.CurrentStop.Value.Should().Be(1.0850m);
        loaded.Legs.Should().BeEmpty();
    }

    // ── IPaperTradeRepository.GetOpenAsync ────────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task PaperTradeRepository_GetOpenAsync_returns_only_open_trades()
    {
        var accountId = Guid.NewGuid();
        await using (var ctx = CreateContext())
        {
            await AccountRepo(ctx).AddAsync(new PaperAccount(accountId, new Money(10_000m), 5m));
            await Uow(ctx).SaveChangesAsync();
        }

        var openId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        var plan = BuildSetup().Plan;

        await using (var ctx = CreateContext())
        {
            var account = (await AccountRepo(ctx).GetByIdAsync(accountId))!;

            var openTrade = new PaperTrade(openId, accountId, new Symbol("EURUSD"),
                TradeStyle.Intraday, Timeframe.M5, plan, new PositionSize(0.1m), 0.0001m, 1m, Epoch);
            account.RegisterOpen(openTrade);

            var closedTrade = new PaperTrade(closedId, accountId, new Symbol("EURUSD"),
                TradeStyle.Intraday, Timeframe.M5, plan, new PositionSize(0.1m), 0.0001m, 1m, Epoch);
            account.RegisterOpen(closedTrade);
            closedTrade.Close(plan.Stop, TradeCloseReason.StopHit, TradeCosts.Zero, Epoch.AddMinutes(5));
            account.Settle(closedTrade);

            var repo = TradeRepo(ctx);
            await repo.AddAsync(openTrade);
            await repo.AddAsync(closedTrade);
            await Uow(ctx).SaveChangesAsync();
        }

        IReadOnlyList<PaperTrade> open;
        await using (var ctx = CreateContext())
        {
            open = await TradeRepo(ctx).GetOpenAsync();
        }

        open.Should().ContainSingle(t => t.Id == openId, "only the open trade is returned");
        open.Should().NotContain(t => t.Id == closedId, "closed trades are excluded");
    }

    // ── IArmedEntryRepository: add + reload ───────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task ArmedEntryRepository_add_and_reload_preserves_setup_snapshot()
    {
        var accountId = Guid.NewGuid();
        await using (var ctx = CreateContext())
        {
            await AccountRepo(ctx).AddAsync(new PaperAccount(accountId, new Money(10_000m), 5m));
            await Uow(ctx).SaveChangesAsync();
        }

        var entryId = Guid.NewGuid();
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
            await EntryRepo(ctx).AddAsync(entry);
            await Uow(ctx).SaveChangesAsync();
        }

        ArmedEntry loaded;
        await using (var ctx = CreateContext())
        {
            loaded = (await EntryRepo(ctx).GetByIdAsync(entryId))!;
        }

        loaded.Should().NotBeNull();
        loaded.Id.Should().Be(entryId);
        loaded.AccountId.Should().Be(accountId);
        loaded.Status.Should().Be(ArmedEntryStatus.Armed);
        loaded.Size.Lots.Should().Be(0.1m);
        loaded.RiskBudget.Amount.Should().Be(50m);
        loaded.PipSize.Should().Be(0.0001m);
        loaded.ValuePerPip.Should().Be(1m);
        loaded.InstrumentClass.Should().Be(InstrumentClass.Fx);
        loaded.ArmedAtUtc.Should().Be(Epoch);

        // Setup JSONB snapshot survived the round-trip.
        loaded.Setup.Symbol.Value.Should().Be("EURUSD");
        loaded.Setup.Grade.Should().Be(SetupGrade.A);
        loaded.Setup.Plan.Entry.Value.Should().Be(1.0900m);
        loaded.Setup.Plan.Stop.Value.Should().Be(1.0850m);
        loaded.Symbol.Value.Should().Be("EURUSD");
        loaded.Direction.Should().Be(Direction.Bullish);
    }

    // ── IArmedEntryRepository.GetActiveAsync ─────────────────────────────────────────────────────────

    [DockerRequiredFact]
    public async Task ArmedEntryRepository_GetActiveAsync_returns_only_armed_entries()
    {
        var accountId = Guid.NewGuid();
        await using (var ctx = CreateContext())
        {
            await AccountRepo(ctx).AddAsync(new PaperAccount(accountId, new Money(10_000m), 5m));
            await Uow(ctx).SaveChangesAsync();
        }

        var armedId = Guid.NewGuid();
        var cancelledId = Guid.NewGuid();
        var setup = BuildSetup();

        await using (var ctx = CreateContext())
        {
            var armed = new ArmedEntry(armedId, accountId, setup, new PositionSize(0.1m),
                new Money(50m), 0.0001m, 1m, InstrumentClass.Fx, Epoch);
            var cancelled = new ArmedEntry(cancelledId, accountId, setup, new PositionSize(0.1m),
                new Money(50m), 0.0001m, 1m, InstrumentClass.Fx, Epoch);
            cancelled.Cancel(EntryCancelReason.KillzoneEnded, Epoch.AddMinutes(15));

            var repo = EntryRepo(ctx);
            await repo.AddAsync(armed);
            await repo.AddAsync(cancelled);
            await Uow(ctx).SaveChangesAsync();
        }

        IReadOnlyList<ArmedEntry> active;
        await using (var ctx = CreateContext())
        {
            active = await EntryRepo(ctx).GetActiveAsync();
        }

        active.Should().ContainSingle(e => e.Id == armedId, "only the Armed entry is returned");
        active.Should().NotContain(e => e.Id == cancelledId, "Cancelled entries are excluded");
    }

    // ── Mutate-and-save round-trip: open → settle → reload and assert ─────────────────────────────────

    [DockerRequiredFact]
    public async Task Mutate_and_save_round_trip_open_trade_settle_persists_closed_state()
    {
        var accountId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var plan = BuildSetup().Plan;

        // Step 1: create account + open trade.
        await using (var ctx = CreateContext())
        {
            var account = new PaperAccount(accountId, new Money(10_000m), 5m);
            var trade = new PaperTrade(
                tradeId, accountId, new Symbol("EURUSD"),
                TradeStyle.Intraday, Timeframe.M5, plan,
                new PositionSize(0.1m), 0.0001m, 1m, Epoch);
            account.RegisterOpen(trade);
            await AccountRepo(ctx).AddAsync(account);
            await TradeRepo(ctx).AddAsync(trade);
            await Uow(ctx).SaveChangesAsync();
        }

        // Step 2: reload, close the trade, settle the account — second SaveChangesAsync.
        await using (var ctx = CreateContext())
        {
            var account = (await AccountRepo(ctx).GetByIdAsync(accountId))!;
            var trade = (await TradeRepo(ctx).GetByIdAsync(tradeId))!;

            // Runner at T2: +100 pips = +2R on a 50-pip stop (0.1 lots, pip=$1).
            var costs = new TradeCosts(new Money(0.07m), new Money(3m));
            trade.Close(new Price(1.1000m), TradeCloseReason.TargetHit, costs, Epoch.AddMinutes(60));
            account.Settle(trade);
            await Uow(ctx).SaveChangesAsync();
        }

        // Step 3: reload and verify closed state, fill-leg ledger, realized figures, account equity.
        PaperAccount reloadedAccount;
        PaperTrade reloadedTrade;

        await using (var ctx = CreateContext())
        {
            reloadedAccount = (await AccountRepo(ctx).GetByIdAsync(accountId))!;
            reloadedTrade = (await TradeRepo(ctx).GetByIdAsync(tradeId))!;
        }

        // Trade closed state.
        reloadedTrade.Status.Should().Be(TradeStatus.Closed);
        reloadedTrade.Lifecycle.Should().Be(TradeLifecycle.Closed);
        reloadedTrade.CloseReason.Should().Be(TradeCloseReason.TargetHit);
        reloadedTrade.ClosedAtUtc.Should().Be(Epoch.AddMinutes(60));
        reloadedTrade.ExitPrice!.Value.Value.Should().Be(1.1000m);

        // Fill-leg ledger: one final leg.
        reloadedTrade.Legs.Should().HaveCount(1, "a single full-size final leg");
        reloadedTrade.Legs[0].ExitPrice.Value.Should().Be(1.1000m);
        reloadedTrade.Legs[0].Lots.Lots.Should().Be(0.1m);

        // GrossPnl: (1.1000−1.0900)/0.0001 × 1 × 0.1 = 100 pips × 0.1 lots = $10.00
        reloadedTrade.GrossPnl!.Value.Amount.Should().BeApproximately(10m, 0.001m, "100 pips × 0.1 lots");
        // Costs: 0.07 + 3.00 = $3.07
        reloadedTrade.Costs!.Value.Amount.Should().BeApproximately(3.07m, 0.001m);
        reloadedTrade.RealizedPnl!.Value.Amount.Should().BeApproximately(10m - 3.07m, 0.001m);

        // RiskBudget = |entry−stop| / pipSize × VPP × lots = 0.005/0.0001 × 1 × 0.1 = $5.00
        // RealizedR = GrossPnl / RiskBudget = 10 / 5 = 2.0 (gross structural R, per §5.2)
        reloadedTrade.RiskBudget.Amount.Should().BeApproximately(5m, 0.001m);
        reloadedTrade.RealizedR.Should().BeApproximately(2.0m, 0.001m, "+2R at T2");

        // Account: reservation released (OpenRisk == 0), equity advanced by NetPnl.
        reloadedAccount.OpenRisk.Amount.Should().Be(0m, "settled trade reservation released");
        reloadedAccount.Equity.Amount.Should().BeApproximately(10_000m + (10m - 3.07m), 0.01m);

        // Adaptive-risk state: 1 consecutive win, 0 losses (gross outcome was positive).
        reloadedAccount.RiskState.ConsecutiveWins.Should().Be(1);
        reloadedAccount.RiskState.ConsecutiveLosses.Should().Be(0);

        // GetOpenAsync must NOT include the now-closed trade.
        await using (var ctx = CreateContext())
        {
            var open = await TradeRepo(ctx).GetOpenAsync();
            open.Should().NotContain(t => t.Id == tradeId, "settled trade is no longer open");
        }
    }
}
