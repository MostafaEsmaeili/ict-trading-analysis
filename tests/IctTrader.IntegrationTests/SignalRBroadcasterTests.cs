using IctTrader.Host.Hubs;
using IctTrader.Host.Realtime;
using IctTrader.MarketData.Contracts;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Performance.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Locks the bus -> SignalR live-push bridge (plan §9 / WP7). The Host-resident <c>*Broadcaster</c> handlers
/// subscribe to the in-memory bus integration events and push each to the dashboard over the push-only
/// <see cref="TradingHub"/>. These tests drive each broadcaster with a hand-rolled fake
/// <see cref="IHubContext{THub}"/> (Moq is not available; FluentAssertions 7.x + a fake suffice) and assert
/// (1) the correct client-method name + payload reach the hub, and (2) a thrown push is swallowed so the bus
/// dispatch chain (candle ingestion / trade settlement) is never broken.
/// </summary>
public sealed class SignalRBroadcasterTests
{
    private static readonly CandleDto Candle =
        new("EURUSD", "M5", DateTimeOffset.UnixEpoch, 1.08m, 1.09m, 1.07m, 1.085m, 100m);

    private static readonly SetupDto Setup = new(
        Id: Guid.NewGuid(), Symbol: "EURUSD", Direction: "Bullish", Killzone: "LondonOpen", Style: "Intraday",
        Grade: "B", TriggerTimeframe: "M5", Entry: 1.0832m, Stop: 1.0800m, Targets: [1.0876m, 1.0920m],
        RewardRatio: 2.75m, Reason: "bias; sweep; MSS; FVG; OTE", DetectedAtUtc: DateTimeOffset.UnixEpoch,
        IsAdvisoryOnly: true);

    private static readonly PaperTradeDto Trade = new(
        Id: Guid.NewGuid(), SetupId: Guid.NewGuid(), Symbol: "EURUSD", Direction: "Bullish", Status: "Open",
        Style: "Intraday", Killzone: "LondonOpen", Entry: 1.0832m, Stop: 1.0800m, Targets: [1.0876m],
        Size: 0.1m, OpenedAtUtc: DateTimeOffset.UnixEpoch, ClosedAtUtc: null, RealizedR: null);

    private static readonly PerformanceSummaryDto Summary = new(
        TradeCount: 3, WinRate: 0.66m, AverageR: 1.2m, ProfitFactor: 2.1m, Expectancy: 0.8m, MaxDrawdown: -1.5m);

    [Fact]
    public async Task CandleIngestedBroadcaster_pushes_CandleAppended_with_the_candle()
    {
        var (hub, capture) = FakeHub();
        await new CandleIngestedBroadcaster(hub, NullLogger<CandleIngestedBroadcaster>.Instance)
            .HandleAsync(new CandleIngested(Candle));

        capture.Method.Should().Be(TradingHub.CandleAppended);
        capture.Args.Should().ContainSingle().Which.Should().BeSameAs(Candle);
    }

    [Fact]
    public async Task SetupConfirmedBroadcaster_pushes_SetupDetected_with_the_setup()
    {
        var (hub, capture) = FakeHub();
        await new SetupConfirmedBroadcaster(hub, NullLogger<SetupConfirmedBroadcaster>.Instance)
            .HandleAsync(new SetupConfirmed(Setup));

        capture.Method.Should().Be(TradingHub.SetupDetected);
        capture.Args.Should().ContainSingle().Which.Should().BeSameAs(Setup);
    }

    [Fact]
    public async Task PaperTradeOpenedBroadcaster_pushes_TradeUpdated_with_the_trade()
    {
        var (hub, capture) = FakeHub();
        await new PaperTradeOpenedBroadcaster(hub, NullLogger<PaperTradeOpenedBroadcaster>.Instance)
            .HandleAsync(new PaperTradeOpened(Trade));

        capture.Method.Should().Be(TradingHub.TradeUpdated);
        capture.Args.Should().ContainSingle().Which.Should().BeSameAs(Trade);
    }

    [Fact]
    public async Task PaperTradeClosedBroadcaster_pushes_TradeUpdated_with_the_trade()
    {
        var (hub, capture) = FakeHub();
        await new PaperTradeClosedBroadcaster(hub, NullLogger<PaperTradeClosedBroadcaster>.Instance)
            .HandleAsync(new PaperTradeClosed(Trade, "Win"));

        capture.Method.Should().Be(TradingHub.TradeUpdated);
        capture.Args.Should().ContainSingle().Which.Should().BeSameAs(Trade);
    }

    [Fact]
    public async Task PerformanceUpdatedBroadcaster_pushes_PerformanceUpdated_with_the_summary()
    {
        var (hub, capture) = FakeHub();
        await new PerformanceUpdatedBroadcaster(hub, NullLogger<PerformanceUpdatedBroadcaster>.Instance)
            .HandleAsync(new PerformanceUpdated(Summary));

        capture.Method.Should().Be(TradingHub.PerformanceUpdated);
        capture.Args.Should().ContainSingle().Which.Should().BeSameAs(Summary);
    }

    [Fact]
    public async Task A_thrown_push_is_swallowed_so_the_bus_dispatch_chain_is_never_broken()
    {
        // A transport failure (a faulty client connection) must NOT abort candle ingestion or settlement.
        var hub = new ThrowingHubContext();
        var broadcaster = new CandleIngestedBroadcaster(hub, NullLogger<CandleIngestedBroadcaster>.Instance);

        var act = async () => await broadcaster.HandleAsync(new CandleIngested(Candle));

        await act.Should().NotThrowAsync("a dashboard push failure is advisory-only and must not break the chain");
    }

    [Fact]
    public void All_six_broadcasters_register_as_event_handlers_in_the_host_assembly_scan()
    {
        // AddMessaging Scrutor-scans the Host assembly (Program wires it in), so every broadcaster resolves under
        // its closed IEventHandler. This proves the live push fans out alongside the existing module handlers.
        var services = new ServiceCollection()
            .AddSingleton<IHubContext<TradingHub>>(FakeHub().Hub)
            .AddLogging()
            .AddMessaging(typeof(CandleIngestedBroadcaster).Assembly)
            .BuildServiceProvider();

        services.GetServices<IEventHandler<CandleIngested>>().Should().ContainSingle(h => h is CandleIngestedBroadcaster);
        services.GetServices<IEventHandler<SetupConfirmed>>().Should().ContainSingle(h => h is SetupConfirmedBroadcaster);
        services.GetServices<IEventHandler<PaperTradeOpened>>().Should().ContainSingle(h => h is PaperTradeOpenedBroadcaster);
        services.GetServices<IEventHandler<PaperTradeClosed>>().Should().ContainSingle(h => h is PaperTradeClosedBroadcaster);
        services.GetServices<IEventHandler<PerformanceUpdated>>().Should().ContainSingle(h => h is PerformanceUpdatedBroadcaster);
    }

    // ---- Hand-rolled SignalR fakes (Moq is not available in this repo) ----

    private static (IHubContext<TradingHub> Hub, SendCapture Capture) FakeHub()
    {
        var capture = new SendCapture();
        return (new CapturingHubContext(capture), capture);
    }

    private sealed class SendCapture
    {
        public string? Method { get; private set; }
        public object?[] Args { get; private set; } = [];

        public void Record(string method, object?[] args)
        {
            Method = method;
            Args = args;
        }
    }

    /// <summary>An <see cref="IHubContext{THub}"/> whose <c>Clients.All</c> records the pushed method + args.</summary>
    private sealed class CapturingHubContext(SendCapture capture) : IHubContext<TradingHub>
    {
        public IHubClients Clients { get; } = new CapturingClients(capture);
        public IGroupManager Groups { get; } = new NoOpGroupManager();
    }

    private sealed class CapturingClients(SendCapture capture) : IHubClients
    {
        private readonly IClientProxy _all = new CapturingClientProxy(capture);
        public IClientProxy All => _all;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _all;
        public IClientProxy Client(string connectionId) => _all;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _all;
        public IClientProxy Group(string groupName) => _all;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _all;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _all;
        public IClientProxy User(string userId) => _all;
        public IClientProxy Users(IReadOnlyList<string> userIds) => _all;
    }

    private sealed class CapturingClientProxy(SendCapture capture) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            capture.Record(method, args);
            return Task.CompletedTask;
        }
    }

    /// <summary>An <see cref="IHubContext{THub}"/> whose push always throws — to prove the broadcaster swallows it.</summary>
    private sealed class ThrowingHubContext : IHubContext<TradingHub>
    {
        public IHubClients Clients { get; } = new ThrowingClients();
        public IGroupManager Groups { get; } = new NoOpGroupManager();
    }

    private sealed class ThrowingClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new ThrowingClientProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class ThrowingClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated transport failure");
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
