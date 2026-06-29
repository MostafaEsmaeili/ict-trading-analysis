using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;
using IctTrader.PaperTrading.Application;
using IctTrader.PaperTrading.Application.Trading;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PaperTradeOpenedEvent = IctTrader.PaperTrading.Contracts.PaperTradeOpened;

namespace IctTrader.UnitTests.PaperTrading;

/// <summary>
/// Locks the semi-auto MANUAL "TAKE" slice (plan §15 — the operator's "give me the opportunity to use that setup")
/// end-to-end over the REAL bus into the REAL handlers (<see cref="SetupConfirmedHandler"/> +
/// <see cref="TakeSetupCommandHandler"/>) → the shared <see cref="SetupTradeOpener"/> → the pinned domain
/// <see cref="TradeOrchestrator"/>:
/// <list type="bullet">
/// <item><b>Manual</b> → a confirmed setup PENDS (no trade, no risk reserved);</item>
/// <item><b>Auto</b> → a confirmed setup opens immediately (unchanged — the SetupTradeOpener path);</item>
/// <item><b>Take</b> → opens exactly one trade with IDENTICAL sizing to Auto; a double-take is a no-op; an
/// unknown/expired id fails; the open routes through the SAME opener as Auto.</item>
/// </list>
/// The harness wires the production handlers over in-memory fake repos + the real pending store (no Postgres → a
/// UnitTest), exactly like <c>PaperTradingFlowTests</c>.
/// </summary>
public class TakeWorkflowTests
{
    private static readonly Symbol Eurusd = new("EURUSD");

    // 07:00 UTC on 2024-07-01 = 03:00 NY = inside the London Open killzone (NY is UTC-4 in July).
    private static readonly DateTimeOffset Confirmed = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static SetupDto BullishSetupDto(Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
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

    // ---- SetupConfirmedHandler entry-mode branch ----------------------------------------------------------------

    [Fact]
    public async Task Manual_mode_records_a_pending_and_opens_no_trade()
    {
        using var harness = new Harness(TradeEntryMode.Manual);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();
        var setup = BullishSetupDto();

        await bus.PublishAsync(new SetupConfirmed(setup));

        // Nothing opened, nothing armed, no risk reserved — only a pending opportunity exists.
        harness.OpenedEvents.Should().BeEmpty("a Manual setup does not open until the operator takes it");
        harness.Trades.Saved.Should().BeEmpty();
        harness.ArmedEntries.Saved.Should().BeEmpty();
        harness.Accounts.AccountOrNull.Should().BeNull("a pending reserves no risk, so the account is never created");
        harness.Pending.IsPending(setup.Id, Confirmed).Should().BeTrue();
    }

    [Fact]
    public async Task Auto_mode_opens_a_trade_immediately_unchanged()
    {
        using var harness = new Harness(TradeEntryMode.Auto, EntryMode.Immediate);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();
        var setup = BullishSetupDto();

        await bus.PublishAsync(new SetupConfirmed(setup));

        // Same as before the slice: one trade opened, risk reserved, no pending.
        harness.OpenedEvents.Should().ContainSingle();
        harness.Trades.Saved.Should().ContainSingle();
        harness.Trades.Saved.Single().Id.Should().Be(setup.Id);
        harness.Account().OpenRisk.Amount.Should().BeGreaterThan(0m);
        harness.Pending.IsPending(setup.Id, Confirmed).Should().BeFalse();
    }

    // ---- TakeSetupCommandHandler -------------------------------------------------------------------------------

    [Fact]
    public async Task Take_opens_exactly_one_trade_with_identical_sizing_to_auto()
    {
        // Size the SAME setup both ways and prove the lot size matches byte-for-byte (the shared SetupTradeOpener).
        decimal autoSize;
        using (var auto = new Harness(TradeEntryMode.Auto, EntryMode.Immediate))
        {
            var bus = auto.Provider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new SetupConfirmed(BullishSetupDto()));
            autoSize = auto.Trades.Saved.Single().Size.Lots;
        }

        using var take = new Harness(TradeEntryMode.Manual, EntryMode.Immediate);
        var takeBus = take.Provider.GetRequiredService<IMessageBus>();
        var setup = BullishSetupDto();

        // Confirm in Manual → pend. Nothing opens.
        await takeBus.PublishAsync(new SetupConfirmed(setup));
        take.Trades.Saved.Should().BeEmpty();

        // TAKE it → exactly one trade opens, sized identically to the auto path.
        await takeBus.SendAsync(new TakeSetupCommand(setup.Id));

        take.OpenedEvents.Should().ContainSingle("the take opens exactly one trade");
        var trade = take.Trades.Saved.Single();
        trade.Id.Should().Be(setup.Id, "the taken trade carries the deterministic seam id");
        trade.Size.Lots.Should().Be(autoSize, "Take and Auto share the SetupTradeOpener, so sizing is byte-identical");
        take.Pending.IsPending(setup.Id, Confirmed).Should().BeFalse("the take consumed the pending");
    }

    [Fact]
    public async Task Taking_the_same_setup_twice_opens_exactly_one_trade()
    {
        using var harness = new Harness(TradeEntryMode.Manual, EntryMode.Immediate);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();
        var setup = BullishSetupDto();

        await bus.PublishAsync(new SetupConfirmed(setup));
        await bus.SendAsync(new TakeSetupCommand(setup.Id));
        harness.Trades.Saved.Should().ContainSingle();
        var openRiskAfterOne = harness.Account().OpenRisk.Amount;

        // A second take of the same id: the pending is gone and a trade exists → AlreadyTaken (no second trade).
        var secondTake = async () => await bus.SendAsync(new TakeSetupCommand(setup.Id));
        await secondTake.Should().ThrowAsync<TakeSetupException>()
            .Where(e => e.Reason == TakeSetupFailure.AlreadyTaken);

        harness.Trades.Saved.Should().ContainSingle("a double-take must not open a second trade");
        harness.Account().OpenRisk.Amount.Should().Be(openRiskAfterOne, "the cap reservation did not double");
    }

    [Fact]
    public async Task Taking_an_unknown_setup_fails_with_NotFound()
    {
        using var harness = new Harness(TradeEntryMode.Manual);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();

        var take = async () => await bus.SendAsync(new TakeSetupCommand(Guid.NewGuid()));

        await take.Should().ThrowAsync<TakeSetupException>().Where(e => e.Reason == TakeSetupFailure.NotFound);
        harness.Trades.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Taking_an_expired_pending_fails_and_opens_nothing()
    {
        // A 1-minute pending window so the pending ages out before the take.
        using var harness = new Harness(TradeEntryMode.Manual, maxPendingMinutes: 1);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();
        var setup = BullishSetupDto();

        await bus.PublishAsync(new SetupConfirmed(setup));
        harness.Pending.IsPending(setup.Id, Confirmed).Should().BeTrue();

        // Advance the clock past the window so TryTake (which uses the injected TimeProvider) sees it as expired.
        harness.Clock.SetUtcNow(Confirmed.AddMinutes(5));

        var take = async () => await bus.SendAsync(new TakeSetupCommand(setup.Id));
        // It was PRESENT then aged out → Expired (409), distinct from a never-known id → NotFound (404).
        await take.Should().ThrowAsync<TakeSetupException>().Where(e => e.Reason == TakeSetupFailure.Expired);
        harness.Trades.Saved.Should().BeEmpty("an expired pending opens nothing");
    }

    [Fact]
    public async Task Take_path_publishes_a_PaperTradeOpened_just_like_auto()
    {
        using var harness = new Harness(TradeEntryMode.Manual, EntryMode.Immediate);
        var bus = harness.Provider.GetRequiredService<IMessageBus>();
        var setup = BullishSetupDto();

        await bus.PublishAsync(new SetupConfirmed(setup));
        await bus.SendAsync(new TakeSetupCommand(setup.Id));

        harness.OpenedEvents.Should().ContainSingle();
        var opened = harness.OpenedEvents.Single().Trade;
        opened.Id.Should().Be(setup.Id);
        opened.Status.Should().Be(TradeStatus.Open.ToString());
        // The wire carries the TRADE side (Long/Short), not the structural Bullish/Bearish.
        opened.Direction.Should().Be(Direction.Bullish.ToTradeDirection().ToString());
    }

    // ---- SetupTradeOpener (the shared open path) directly -------------------------------------------------------

    [Fact]
    public async Task SetupTradeOpener_auto_path_opens_and_publishes_unchanged()
    {
        using var harness = new Harness(TradeEntryMode.Auto, EntryMode.Immediate);
        using var scope = harness.Provider.CreateScope();
        var opener = scope.ServiceProvider.GetRequiredService<SetupTradeOpener>();
        var setup = BullishSetupDto();

        var dto = await opener.OpenAsync(setup, CancellationToken.None);

        dto.Should().NotBeNull("an Immediate open returns the opened DTO");
        dto!.Id.Should().Be(setup.Id);
        harness.Trades.Saved.Single().Id.Should().Be(setup.Id);
        harness.OpenedEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task SetupTradeOpener_armed_path_arms_and_returns_null()
    {
        using var harness = new Harness(TradeEntryMode.Auto, EntryMode.Armed);
        using var scope = harness.Provider.CreateScope();
        var opener = scope.ServiceProvider.GetRequiredService<SetupTradeOpener>();
        var setup = BullishSetupDto();

        var dto = await opener.OpenAsync(setup, CancellationToken.None);

        dto.Should().BeNull("an Armed setup rests a limit, so there is no opened trade to echo");
        harness.ArmedEntries.Saved.Should().ContainSingle();
        harness.OpenedEvents.Should().BeEmpty();
    }

    // ---- Guardrail: the take path exposes no order/broker/execute member ----------------------------------------

    [Fact]
    public void The_take_path_types_expose_no_order_broker_or_execute_member()
    {
        // Defensive guardrail (§6.3): the semi-auto TAKE slice must add NO live-execution surface. Scan the public +
        // non-public members of every type the take path introduces for a forbidden order/broker/execute/buy/sell name.
        var takePathTypes = new[]
        {
            typeof(SetupTradeOpener),
            typeof(TakeSetupCommandHandler),
            typeof(TakeSetupCommand),
            typeof(PendingOpportunityStore),
            typeof(PendingOpportunityOptions),
            typeof(TradeEntryMode),
        };

        string[] forbidden = ["order", "broker", "execute", "buy", "sell"];

        foreach (var type in takePathTypes)
        {
            var members = type.GetMembers(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);

            foreach (var member in members)
            {
                var lower = member.Name.ToLowerInvariant();
                forbidden.Should().NotContain(
                    f => lower.Contains(f, StringComparison.Ordinal),
                    $"the paper-only TAKE path ({type.Name}.{member.Name}) must expose no live-execution member");
            }
        }
    }

    // ---- Test harness: the real bus + handlers + opener + pending store over in-memory fake repos ---------------

    private sealed class Harness : IDisposable
    {
        public Harness(
            TradeEntryMode tradeEntryMode,
            EntryMode entryMode = EntryMode.Armed,
            int maxPendingMinutes = 240)
        {
            Accounts = new FakeAccountRepository();
            Trades = new FakeTradeRepository();
            ArmedEntries = new FakeArmedEntryRepository();
            Clock = new FakeTimeProvider(Confirmed);

            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);

            // The validated Ict:* options the orchestrator factory + handlers depend on (verified defaults). The
            // TAKE workflow default is tradeEntryMode; the arm-vs-open path is entryMode (Ict:Execution:Entry).
            services.AddSingleton(Options.Create(new FillOptions()));
            services.AddSingleton(Options.Create(new ExecutionCostOptions()));
            services.AddSingleton(Options.Create(new StopTrailOptions()));
            services.AddSingleton(Options.Create(new ExitManagementOptions()));
            services.AddSingleton(Options.Create(new EntryManagementOptions { Mode = entryMode }));
            services.AddSingleton(Options.Create(new KillzoneEntryOptions()));
            services.AddSingleton(Options.Create(new TradeStyleOptions()));
            services.AddSingleton(Options.Create(new RiskOptions()));
            services.AddSingleton(Options.Create(new DailyRiskGuardOptions()));
            services.AddSingleton(Options.Create(new ConfluenceOptions()));
            services.AddSingleton(Options.Create(new PaperTradingOptions { DefaultEntryMode = tradeEntryMode }));
            services.AddSingleton(Options.Create(new PendingOpportunityOptions { MaxPendingMinutes = maxPendingMinutes }));

            // The fake persistence (in-memory; scope-shared so a dispatch's reads see its own writes).
            services.AddScoped<IPaperAccountRepository>(_ => Accounts);
            services.AddScoped<IPaperTradeRepository>(_ => Trades);
            services.AddScoped<IArmedEntryRepository>(_ => ArmedEntries);
            services.AddScoped<IPaperTradingUnitOfWork>(_ => new FakeUnitOfWork());

            // The capturing sink for the published open contract event.
            services.AddSingleton(OpenedEvents);
            services.AddScoped<IEventHandler<PaperTradeOpenedEvent>, CapturingOpenedHandler>();

            services.AddPaperTradingModule();

            // Scan ONLY the PaperTrading.Application assembly for the production handlers; the capturing sink is
            // registered explicitly above.
            services.AddMessaging(typeof(SetupConfirmedHandler).Assembly);

            Provider = services.BuildServiceProvider();
            Pending = Provider.GetRequiredService<PendingOpportunityStore>();
        }

        public ServiceProvider Provider { get; }

        public FakeTimeProvider Clock { get; }

        public FakeAccountRepository Accounts { get; }

        public FakeTradeRepository Trades { get; }

        public FakeArmedEntryRepository ArmedEntries { get; }

        public PendingOpportunityStore Pending { get; }

        public List<PaperTradeOpenedEvent> OpenedEvents { get; } = [];

        public PaperAccount Account() => Accounts.Single;

        public void Dispose() => Provider.Dispose();
    }

    // ---- In-memory fake repositories (a Dictionary IS the database; the orchestrators are stateless) ------------

    private sealed class FakeAccountRepository : IPaperAccountRepository
    {
        private PaperAccount? _account;

        public PaperAccount Single => _account ?? throw new InvalidOperationException("No account created yet.");

        public PaperAccount? AccountOrNull => _account;

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
}
