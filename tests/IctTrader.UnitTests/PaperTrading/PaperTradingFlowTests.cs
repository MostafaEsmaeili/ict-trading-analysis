using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;
using IctTrader.PaperTrading.Application;
using IctTrader.PaperTrading.Application.Trading;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PaperTradeClosedEvent = IctTrader.PaperTrading.Contracts.PaperTradeClosed;
using PaperTradeOpenedEvent = IctTrader.PaperTrading.Contracts.PaperTradeOpened;

namespace IctTrader.UnitTests.PaperTrading;

/// <summary>
/// Locks WP7 slice 2d-iii — the PaperTrading module's trade orchestration on the in-memory bus (plan §3.0a/§4.1).
/// It drives the FULL cycle through the REAL bus into the REAL handlers (<see cref="SetupConfirmedHandler"/> +
/// <see cref="PaperTradingCandleHandler"/>) → the pinned domain <see cref="TradeOrchestrator"/>: a confirmed advisory
/// setup opens/arms a sized trade (reserving its risk against the demo account's cap), and subsequent candles advance
/// it to a settled close — proving the orchestration end-to-end, the DB-as-state persistence (fake in-memory repos,
/// no Postgres so it is a UnitTest), and the published <see cref="PaperTradeOpenedEvent"/>/<see cref="PaperTradeClosedEvent"/>
/// contract events.
/// </summary>
public class PaperTradingFlowTests
{
    private static readonly Symbol Eurusd = new("EURUSD");

    // 07:00 UTC on 2024-07-01 = 03:00 NY = inside the London Open killzone (NY is UTC-4 in July). The runner candle
    // opens within the killzone so the no-chase rung never fires on the armed path.
    private static readonly DateTimeOffset Confirmed = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    // Long: entry 1.0832, stop 1.0800 (32-pip 1R), T1 1.0876, runner 1.0920 (+2.75R) — the proven orchestrator fixture.
    private static SetupDto BullishSetupDto() => new(
        Id: Guid.NewGuid(),
        Symbol: Eurusd.Value,
        Direction: Direction.Bullish.ToString(),
        Killzone: Killzone.LondonOpen.ToString(),
        Style: TradeStyle.Intraday.ToString(),
        Grade: SetupGrade.B.ToString(),
        TriggerTimeframe: Timeframe.M5.ToString(),
        Entry: 1.0832m,
        Stop: 1.0800m,
        Targets: [1.0876m, 1.0920m],
        RewardRatio: 2.75m,
        Reason: "bias; sweep; MSS; FVG; OTE",
        DetectedAtUtc: Confirmed,
        IsAdvisoryOnly: true);

    private static CandleDto Candle(DateTimeOffset openUtc, decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd.Value, Timeframe.M5.ToString(), openUtc, open, high, low, close, 1_000m);

    [Fact]
    public async Task Immediate_setup_opens_a_trade_then_a_runner_candle_closes_and_settles_it()
    {
        using var harness = new Harness(EntryMode.Immediate);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();

        // 1. A confirmed setup opens a trade immediately — reserved against the demo account, persisted, announced.
        await bus.PublishAsync(new SetupConfirmed(BullishSetupDto()));

        harness.OpenedEvents.Should().ContainSingle();
        var openedDto = harness.OpenedEvents.Single().Trade;
        openedDto.Symbol.Should().Be(Eurusd.Value);
        // The wire carries the TRADE side (Long/Short), not the structural Bullish/Bearish (PaperTradeDtoMapper).
        openedDto.Direction.Should().Be(Direction.Bullish.ToTradeDirection().ToString());
        openedDto.Status.Should().Be(TradeStatus.Open.ToString());

        harness.Trades.Saved.Should().ContainSingle();
        harness.Account().OpenRisk.Amount.Should().BeGreaterThan(0m); // the open trade reserves risk
        harness.ClosedEvents.Should().BeEmpty();

        // 2. A later candle reaches the runner (High 1.0925 >= 1.0920) — the trade closes and settles.
        await bus.PublishAsync(new CandleIngested(
            Candle(Confirmed.AddMinutes(5), 1.0900m, 1.0925m, 1.0895m, 1.0915m)));

        harness.ClosedEvents.Should().ContainSingle();
        var closedDto = harness.ClosedEvents.Single();
        closedDto.Trade.Id.Should().Be(openedDto.Id);
        closedDto.Trade.Status.Should().Be(TradeStatus.Closed.ToString());
        closedDto.Trade.RealizedR.Should().BeApproximately(2.75m, 0.0001m);
        closedDto.Outcome.Should().Be(TradeCloseReason.TargetHit.ToString());

        // The account settled: equity moved up, the reservation released, the trade no longer open.
        harness.Account().OpenRisk.Amount.Should().Be(0m);
        harness.Account().Equity.Amount.Should().BeGreaterThan(10_000m);
        (await harness.Trades.GetOpenAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Armed_setup_rests_a_limit_that_a_retrace_candle_fills_then_runs_to_target()
    {
        using var harness = new Harness(EntryMode.Armed);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();

        // 1. A confirmed setup ARMS a resting limit — reserved, persisted, but no open event yet.
        await bus.PublishAsync(new SetupConfirmed(BullishSetupDto()));

        harness.OpenedEvents.Should().BeEmpty();
        harness.ArmedEntries.Saved.Should().ContainSingle();
        harness.Account().OpenRisk.Amount.Should().BeGreaterThan(0m); // the resting limit reserves the cap
        (await harness.Trades.GetOpenAsync()).Should().BeEmpty();

        // 2. A candle whose low never reaches the limit — it keeps resting, nothing opens.
        await bus.PublishAsync(new CandleIngested(
            Candle(Confirmed.AddMinutes(5), 1.0845m, 1.0850m, 1.0838m, 1.0842m)));
        harness.OpenedEvents.Should().BeEmpty();

        // 3. A clean retrace (Low 1.0825 <= entry 1.0832) on the TRIGGER bar (07:10 = bar M) fills the limit and opens
        //    the trade. Its fill time (OpenedAtUtc) is the trigger bar's CLOSE (07:15), but its management-eligibility
        //    edge (ManagedFromUtc) is the trigger bar's OPEN (07:10).
        await bus.PublishAsync(new CandleIngested(
            Candle(Confirmed.AddMinutes(10), 1.0835m, 1.0840m, 1.0825m, 1.0830m)));

        harness.OpenedEvents.Should().ContainSingle();
        (await harness.Trades.GetOpenAsync()).Should().ContainSingle();

        // 4. The CONTIGUOUS next bar (07:15 = bar M+1) reaches the runner — the trade is FIRST managed here (the bar
        //    immediately after the trigger bar), so it closes and settles on M+1, NOT a bar later. The eligibility edge
        //    is the trigger bar's OPEN (07:10), which is strictly before this bar's open (07:15), so M+1 is the first
        //    legitimately-manageable bar — no look-ahead on the trigger bar itself, no off-by-one deferral past it.
        await bus.PublishAsync(new CandleIngested(
            Candle(Confirmed.AddMinutes(15), 1.0900m, 1.0925m, 1.0895m, 1.0915m)));

        harness.ClosedEvents.Should().ContainSingle();
        harness.ClosedEvents.Single().Outcome.Should().Be(TradeCloseReason.TargetHit.ToString());
        harness.Account().OpenRisk.Amount.Should().Be(0m);
        harness.Account().Equity.Amount.Should().BeGreaterThan(10_000m);
    }

    [Fact]
    public async Task Armed_triggered_trade_is_first_managed_on_the_bar_after_the_trigger_no_off_by_one()
    {
        using var harness = new Harness(EntryMode.Armed);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();

        // 1. Arm a resting limit on candle N (07:00).
        await bus.PublishAsync(new SetupConfirmed(BullishSetupDto()));
        harness.ArmedEntries.Saved.Should().ContainSingle();

        // 2. The TRIGGER bar (07:05 = bar M): its Low (1.0825) fills the limit (<= entry 1.0832) but does NOT reach the
        //    stop (1.0800) or runner (1.0920), so the trade opens and survives. The same-bar re-feed manages bar M
        //    legitimately (the bar that filled it), but no exit fires on it.
        await bus.PublishAsync(new CandleIngested(
            Candle(Confirmed.AddMinutes(5), 1.0835m, 1.0840m, 1.0825m, 1.0830m)));
        harness.OpenedEvents.Should().ContainSingle();
        (await harness.Trades.GetOpenAsync()).Should().ContainSingle("the trade opened on the trigger bar and survived");

        // 3. Bar M+1 (07:10), CONTIGUOUS with the trigger bar: a wide bar whose Low pierces the stop (1.0795 <= 1.0800).
        //    Under the corrected eligibility (keyed on the trigger bar's OPEN = 07:05, strictly before this bar's open
        //    07:10) the trade IS managed here — it must stop out on M+1. The old off-by-one (keying on the fill time
        //    07:10 == this bar's open) would skip M+1 and leave the trade open, deferring the stop a full bar to M+2.
        await bus.PublishAsync(new CandleIngested(
            Candle(Confirmed.AddMinutes(10), 1.0820m, 1.0825m, 1.0795m, 1.0805m)));

        harness.ClosedEvents.Should().ContainSingle("the stop-out is evaluated on M+1, not deferred to M+2");
        var closed = harness.ClosedEvents.Single();
        closed.Outcome.Should().Be(TradeCloseReason.StopHit.ToString());
        closed.Trade.RealizedR.Should().BeApproximately(-1m, 0.0001m);
        harness.Account().OpenRisk.Amount.Should().Be(0m);
        (await harness.Trades.GetOpenAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_setup_confirmed_on_candle_N_is_NOT_managed_on_that_same_bar_no_lookahead()
    {
        using var harness = new Harness(EntryMode.Immediate);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();

        // 1. A setup confirmed on candle N opens a trade stamped at candle N's open (Confirmed). On the runnable Host
        //    both the Scanning and PaperTrading handlers subscribe to the SAME CandleIngested; the trade is created
        //    mid-dispatch and would otherwise be advanced on candle N itself. Open it first (the SetupConfirmed seam).
        await bus.PublishAsync(new SetupConfirmed(BullishSetupDto()));
        harness.OpenedEvents.Should().ContainSingle();
        var openedDto = harness.OpenedEvents.Single().Trade;
        openedDto.OpenedAtUtc.Should().Be(Confirmed);

        // 2. Publish a CandleIngested whose open time EQUALS the confirming bar (candle N) — a wide bar that would hit
        //    BOTH the stop (Low 1.0795 <= 1.0800) AND the runner (High 1.0925 >= 1.0920) if it were advanced. Because
        //    the position was opened on THIS bar (OpenedAtUtc == candle.OpenTimeUtc, not strictly before), it must be
        //    excluded from management — no fill/close on its own signal bar. Look-ahead bias is eliminated.
        await bus.PublishAsync(new CandleIngested(
            Candle(Confirmed, 1.0832m, 1.0925m, 1.0795m, 1.0850m)));

        harness.ClosedEvents.Should().BeEmpty();                              // not closed on the confirming bar
        harness.Account().OpenRisk.Amount.Should().BeGreaterThan(0m);          // reservation still held — trade open
        (await harness.Trades.GetOpenAsync()).Should().ContainSingle();        // the trade is still open

        // 3. The NEXT candle (N+1) finally manages it — here a clean runner closes it, proving it WAS eligible once
        //    past its open bar (so the exclusion is the only thing that gated bar N, not a broken pipeline).
        await bus.PublishAsync(new CandleIngested(
            Candle(Confirmed.AddMinutes(5), 1.0900m, 1.0925m, 1.0895m, 1.0915m)));

        harness.ClosedEvents.Should().ContainSingle();
        harness.ClosedEvents.Single().Trade.Id.Should().Be(openedDto.Id);
        (await harness.Trades.GetOpenAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_stop_out_candle_closes_the_trade_at_minus_one_R_and_books_the_loss()
    {
        using var harness = new Harness(EntryMode.Immediate);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new SetupConfirmed(BullishSetupDto()));
        harness.OpenedEvents.Should().ContainSingle();
        var startingEquity = harness.Account().Equity.Amount;

        // A candle whose low pierces the stop (Low 1.0795 <= stop 1.0800) — the trade stops out at exactly -1R
        // (the frozen 1R, §5.2), proving the WIRING settles a LOSS (not just a winner): equity falls, risk releases.
        await bus.PublishAsync(new CandleIngested(
            Candle(Confirmed.AddMinutes(5), 1.0820m, 1.0825m, 1.0795m, 1.0805m)));

        harness.ClosedEvents.Should().ContainSingle();
        var closed = harness.ClosedEvents.Single();
        closed.Outcome.Should().Be(TradeCloseReason.StopHit.ToString());
        closed.Trade.Status.Should().Be(TradeStatus.Closed.ToString());
        closed.Trade.RealizedR.Should().BeApproximately(-1m, 0.0001m); // exactly -1R gross

        harness.Account().Equity.Amount.Should().BeLessThan(startingEquity); // the loss was booked
        harness.Account().OpenRisk.Amount.Should().Be(0m);                   // the reservation released
        (await harness.Trades.GetOpenAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_same_bar_open_then_close_publishes_an_OPEN_event_that_reports_Status_Open()
    {
        using var harness = new Harness(EntryMode.Armed);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();

        // 1. Arm a resting limit on candle N.
        await bus.PublishAsync(new SetupConfirmed(BullishSetupDto()));
        harness.ArmedEntries.Saved.Should().ContainSingle();

        // 2. A later candle (N+1, past the arm bar) whose Low both FILLS the limit (<= entry 1.0832) and pierces the
        //    STOP (<= 1.0800): the entry triggers and the same-bar −1R straddle closes it in ONE advance — the
        //    aggregate raises BOTH PaperTradeOpened and PaperTradeClosed and ends up Closed.
        await bus.PublishAsync(new CandleIngested(
            Candle(Confirmed.AddMinutes(5), 1.0835m, 1.0838m, 1.0795m, 1.0805m)));

        // The open event must report a COHERENT open snapshot (Status=Open, no close fields), NOT the final Closed
        // state — even though the aggregate is already Closed by the time the events are drained.
        harness.OpenedEvents.Should().ContainSingle();
        var opened = harness.OpenedEvents.Single().Trade;
        opened.Status.Should().Be(TradeStatus.Open.ToString());
        opened.ClosedAtUtc.Should().BeNull();
        opened.RealizedR.Should().BeNull();

        // The separate close event carries the realized outcome.
        harness.ClosedEvents.Should().ContainSingle();
        var closed = harness.ClosedEvents.Single();
        closed.Trade.Status.Should().Be(TradeStatus.Closed.ToString());
        closed.Trade.RealizedR.Should().BeApproximately(-1m, 0.0001m);
        closed.Outcome.Should().Be(TradeCloseReason.StopHit.ToString());
    }

    [Fact]
    public async Task Publishing_the_same_SetupConfirmed_twice_opens_exactly_one_trade_idempotent_on_the_seam_id()
    {
        using var harness = new Harness(EntryMode.Immediate);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();

        // The SAME wire setup (one deterministic SetupDto.Id) is delivered twice — a Replay-restart re-stream, a feed
        // redelivery, or a future at-least-once transport. The deterministic id is the idempotency key: the SECOND
        // delivery must be a no-op (no second trade, no second cap reservation).
        var setup = BullishSetupDto();
        await bus.PublishAsync(new SetupConfirmed(setup));
        await bus.PublishAsync(new SetupConfirmed(setup));

        harness.OpenedEvents.Should().ContainSingle("a redelivered setup must not open a second trade");
        harness.Trades.Saved.Should().ContainSingle();
        (await harness.Trades.GetOpenAsync()).Should().ContainSingle();

        // The trade carries the deterministic seam id, and only ONE reservation is held against the portfolio cap.
        harness.Trades.Saved.Single().Id.Should().Be(setup.Id);
        var openRiskAfterOne = harness.Account().OpenRisk.Amount;
        openRiskAfterOne.Should().BeGreaterThan(0m);

        // A third redelivery is still a no-op — the reservation does not grow.
        await bus.PublishAsync(new SetupConfirmed(setup));
        harness.OpenedEvents.Should().ContainSingle();
        harness.Account().OpenRisk.Amount.Should().Be(openRiskAfterOne);
    }

    [Fact]
    public async Task Publishing_the_same_SetupConfirmed_twice_in_armed_mode_arms_exactly_one_resting_limit()
    {
        using var harness = new Harness(EntryMode.Armed);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();

        // Mirror the Immediate idempotency proof on the armed (default) path: the resting limit is keyed by the same
        // deterministic id, so a redelivered setup must not arm a SECOND limit (double-reserving the cap).
        var setup = BullishSetupDto();
        await bus.PublishAsync(new SetupConfirmed(setup));
        var openRiskAfterOne = harness.Account().OpenRisk.Amount;

        await bus.PublishAsync(new SetupConfirmed(setup));

        harness.ArmedEntries.Saved.Should().ContainSingle("a redelivered setup must not arm a second limit");
        harness.ArmedEntries.Saved.Single().Id.Should().Be(setup.Id);
        harness.Account().OpenRisk.Amount.Should().Be(openRiskAfterOne); // the reservation did not double
    }

    // ---- Test harness: the real bus + module wired over in-memory fake repos (no Postgres) ----------------------

    private sealed class Harness : IDisposable
    {
        public Harness(EntryMode mode)
        {
            Accounts = new FakeAccountRepository();
            Trades = new FakeTradeRepository();
            ArmedEntries = new FakeArmedEntryRepository();

            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Confirmed));

            // The validated Ict:* options the orchestrator factory + handlers depend on (verified defaults; Immediate
            // vs Armed via the entry mode). The runner candle path needs the StopFirst fill assumption (default).
            services.AddSingleton(Options.Create(new FillOptions()));
            services.AddSingleton(Options.Create(new ExecutionCostOptions()));
            services.AddSingleton(Options.Create(new StopTrailOptions()));
            services.AddSingleton(Options.Create(new ExitManagementOptions()));
            services.AddSingleton(Options.Create(new EntryManagementOptions { Mode = mode }));
            services.AddSingleton(Options.Create(new KillzoneEntryOptions()));
            services.AddSingleton(Options.Create(new TradeStyleOptions()));
            services.AddSingleton(Options.Create(new RiskOptions()));
            services.AddSingleton(Options.Create(new ConfluenceOptions()));
            services.AddSingleton(Options.Create(new PaperTradingOptions()));

            // The fake persistence (in-memory; scope-shared so a dispatch's reads see its own writes).
            services.AddScoped<IPaperAccountRepository>(_ => Accounts);
            services.AddScoped<IPaperTradeRepository>(_ => Trades);
            services.AddScoped<IArmedEntryRepository>(_ => ArmedEntries);
            services.AddScoped<IPaperTradingUnitOfWork>(_ => new FakeUnitOfWork());

            // The capturing sinks for the published contract events.
            services.AddSingleton(OpenedEvents);
            services.AddSingleton(ClosedEvents);
            services.AddScoped<IEventHandler<PaperTradeOpenedEvent>, CapturingOpenedHandler>();
            services.AddScoped<IEventHandler<PaperTradeClosedEvent>, CapturingClosedHandler>();

            services.AddPaperTradingModule();

            // Scan ONLY the PaperTrading.Application assembly for the two production handlers; the capturing sinks
            // are registered explicitly above.
            services.AddMessaging(typeof(SetupConfirmedHandler).Assembly);

            Provider = services.BuildServiceProvider();
        }

        public ServiceProvider Provider { get; }

        public FakeAccountRepository Accounts { get; }

        public FakeTradeRepository Trades { get; }

        public FakeArmedEntryRepository ArmedEntries { get; }

        public List<PaperTradeOpenedEvent> OpenedEvents { get; } = [];

        public List<PaperTradeClosedEvent> ClosedEvents { get; } = [];

        public PaperAccount Account() => Accounts.Single;

        public void Dispose() => Provider.Dispose();
    }

    // ---- In-memory fake repositories (a Dictionary IS the database; the orchestrators are stateless) ------------

    private sealed class FakeAccountRepository : IPaperAccountRepository
    {
        private PaperAccount? _account;

        public PaperAccount Single => _account ?? throw new InvalidOperationException("No account created yet.");

        public Task<PaperAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_account is not null && _account.Id == id ? _account : null);

        public Task AddAsync(PaperAccount account, CancellationToken cancellationToken = default)
        {
            _account = account;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTradeRepository : IPaperTradeRepository
    {
        private readonly Dictionary<Guid, PaperTrade> _trades = [];

        public IReadOnlyCollection<PaperTrade> Saved => _trades.Values;

        public Task<PaperTrade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_trades.GetValueOrDefault(id));

        public Task AddAsync(PaperTrade trade, CancellationToken cancellationToken = default)
        {
            _trades[trade.Id] = trade;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PaperTrade>> GetOpenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PaperTrade>>(
                _trades.Values.Where(t => t.Status == TradeStatus.Open).ToList());

        public Task<IReadOnlyList<PaperTrade>> GetClosedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PaperTrade>>(
                _trades.Values.Where(t => t.Status == TradeStatus.Closed)
                    .OrderByDescending(t => t.ClosedAtUtc).ToList());

        public Task<IReadOnlyList<PaperTrade>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PaperTrade>>(
                _trades.Values.OrderByDescending(t => t.OpenedAtUtc).ToList());
    }

    private sealed class FakeArmedEntryRepository : IArmedEntryRepository
    {
        private readonly Dictionary<Guid, ArmedEntry> _entries = [];

        public IReadOnlyCollection<ArmedEntry> Saved => _entries.Values;

        public Task<ArmedEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_entries.GetValueOrDefault(id));

        public Task AddAsync(ArmedEntry entry, CancellationToken cancellationToken = default)
        {
            _entries[entry.Id] = entry;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ArmedEntry>> GetActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ArmedEntry>>(
                _entries.Values.Where(e => e.Status == ArmedEntryStatus.Armed).ToList());
    }

    private sealed class FakeUnitOfWork : IPaperTradingUnitOfWork
    {
        // The fakes mutate their dictionaries in place (the loaded aggregates are the tracked instances), so a
        // commit is a no-op — exactly the DB-as-state model, minus the EF round-trip.
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CapturingOpenedHandler(List<PaperTradeOpenedEvent> captured)
        : IEventHandler<PaperTradeOpenedEvent>
    {
        public Task HandleAsync(PaperTradeOpenedEvent @event, CancellationToken cancellationToken = default)
        {
            captured.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingClosedHandler(List<PaperTradeClosedEvent> captured)
        : IEventHandler<PaperTradeClosedEvent>
    {
        public Task HandleAsync(PaperTradeClosedEvent @event, CancellationToken cancellationToken = default)
        {
            captured.Add(@event);
            return Task.CompletedTask;
        }
    }
}
