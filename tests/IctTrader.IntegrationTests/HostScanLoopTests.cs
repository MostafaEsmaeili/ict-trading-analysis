using System.Net.Http.Json;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.Host;
using IctTrader.PaperTrading.Application.Trading;
using IctTrader.PaperTrading.Contracts;
using IctTrader.PaperTrading.Infrastructure.Persistence;
using IctTrader.Scanning.Application.Scanning;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Locks WP7 slice 2e — the runnable Host composition root (plan §3.0a/§11). Boots the REAL Host via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> against a Testcontainers Postgres so the FULL DI graph composes —
/// the in-memory bus, the Scanning + PaperTrading modules, the EF <see cref="PaperTradingDbContext"/>, and the replay
/// feed (left idle; the bus is driven directly). Then it proves the PaperTrading handler + DbContext + persistence are
/// correctly wired in the Host by publishing a confirmed advisory <see cref="SetupConfirmed"/> and asserting the
/// resulting paper trade was persisted to the REAL Postgres through the REAL Host DI.
/// </summary>
[Collection("PaperTradingDb")]
public sealed class HostScanLoopTests : IAsyncLifetime
{
    private readonly PaperTradingDbFixture _fixture;

    public HostScanLoopTests(PaperTradingDbFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // 07:00 UTC on 2024-07-01 = 03:00 NY = inside the London Open killzone (NY is UTC-4 in July).
    private static readonly DateTimeOffset Confirmed = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    // The proven orchestrator fixture (mirrors PaperTradingFlowTests.BullishSetupDto): long, 32-pip 1R, +2.75R runner.
    private static SetupDto BullishSetupDto() => new(
        Id: Guid.NewGuid(),
        Symbol: "EURUSD",
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

    [DockerRequiredFact]
    public async Task Host_composes_the_full_scan_loop_di_graph_with_the_guardrail_intact()
    {
        await using var factory = CreateFactory();

        // Booting the factory builds the Host and runs ValidateOnStart over every Ict:* options POCO.
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        // The full scan-loop DI graph resolves — the bus, both module registries, and the persistence repositories.
        var resolve = () =>
        {
            _ = services.GetRequiredService<IMessageBus>();
            _ = services.GetRequiredService<ISymbolScannerRegistry>();
            _ = services.GetRequiredService<ITradeOrchestratorRegistry>();
            _ = services.GetRequiredService<IPaperTradeRepository>();
        };
        resolve.Should().NotThrow("the runnable scan-loop composition root must compose end-to-end");

        // The NON-NEGOTIABLE guardrail: the host runs analysis + paper only.
        services.GetRequiredService<IOptions<DefensiveOptions>>().Value.LiveTradingEnabled.Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task A_confirmed_setup_published_on_the_bus_persists_a_paper_trade_through_the_real_host()
    {
        // Immediate mode so the confirmed setup opens a trade directly (GetOpenAsync then returns it).
        await using var factory = CreateFactory(("Ict:Execution:Entry:Mode", "Immediate"));

        // The shared fixture already migrated the schema once on boot, and Respawn truncated the data between tests, so
        // the container DB has the tables and a clean slate. We confirm the Host's OWN context reaches that schema (it
        // queries an existing, empty table — a no-op read that would throw if the connection-string wiring were wrong).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaperTradingDbContext>();
            (await db.PaperTrades.AnyAsync()).Should().BeFalse("the slate is clean before the setup is published");
        }

        // Drive the bus directly — the SetupConfirmed handler runs in its own dispatch scope, opening + persisting
        // the trade against the demo account through the REAL Host DI + the REAL Postgres.
        var bus = factory.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new SetupConfirmed(BullishSetupDto()));

        // Assert via a fresh repository scope: the open trade was committed to Postgres (a fresh context reads the row).
        await using var assertScope = factory.Services.CreateAsyncScope();
        var trades = assertScope.ServiceProvider.GetRequiredService<IPaperTradeRepository>();
        var open = await trades.GetOpenAsync();

        open.Should().ContainSingle("the confirmed advisory setup opened exactly one paper trade");
        var trade = open.Single();
        trade.Symbol.Value.Should().Be("EURUSD");
        trade.Plan.Direction.Should().Be(Direction.Bullish);
        trade.AccountId.Should().Be(PaperAccountProvider.DemoAccountId);
    }

    [DockerRequiredFact]
    public async Task The_active_trades_query_returns_the_open_trade_as_a_dto_over_the_bus()
    {
        // Immediate mode so the confirmed setup opens a trade directly; the bus-backed read-side then returns it.
        await using var factory = CreateFactory(("Ict:Execution:Entry:Mode", "Immediate"));

        var bus = factory.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new SetupConfirmed(BullishSetupDto()));

        // The GetActiveTradesQuery routes over the bus to the PaperTrading query handler (REST → bus → handler).
        var active = await bus.QueryAsync(new GetActiveTradesQuery());

        active.Should().ContainSingle("the bus read-side projects the one open trade to a DTO");
        var dto = active.Single();
        dto.Symbol.Should().Be("EURUSD");
        // The paper-trade wire carries the TRADE side (Long/Short), not the structural Bullish/Bearish.
        dto.Direction.Should().Be(Direction.Bullish.ToTradeDirection().ToString());
        dto.Status.Should().Be(TradeStatus.Open.ToString());
        dto.Entry.Should().Be(1.0832m);
        dto.Stop.Should().Be(1.0800m);
    }

    [DockerRequiredFact]
    public async Task The_active_trades_rest_endpoint_returns_the_open_trade_as_a_dto()
    {
        // Immediate mode so the confirmed setup opens a trade directly; GET /api/trades/active then returns it.
        await using var factory = CreateFactory(("Ict:Execution:Entry:Mode", "Immediate"));

        var bus = factory.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new SetupConfirmed(BullishSetupDto()));

        using var client = factory.CreateClient();
        var dtos = await client.GetFromJsonAsync<IReadOnlyList<PaperTradeDto>>("/api/trades/active");

        dtos.Should().ContainSingle("the REST endpoint now returns real persisted active trades, not an empty stub");
        dtos!.Single().Symbol.Should().Be("EURUSD");
        dtos.Single().Status.Should().Be(TradeStatus.Open.ToString());
    }

    private WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] overrides) =>
        new HostFactory(_fixture.ConnectionString, overrides);

    /// <summary>
    /// Hosts the real <c>Program</c>, overriding the PaperTrading connection string with the Testcontainers DB and
    /// pinning the replay feed OFF (the tests drive the bus directly, so the background scanner must stay idle).
    /// </summary>
    private sealed class HostFactory(string connectionString, (string Key, string Value)[] overrides)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:PaperTrading", connectionString);
            builder.UseSetting($"{ReplayFeedOptions.SectionName}:Enabled", "false");
            foreach (var (key, value) in overrides)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
