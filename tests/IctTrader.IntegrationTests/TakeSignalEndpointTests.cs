using System.Net;
using System.Net.Http.Json;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.Host;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Locks the semi-auto MANUAL TAKE slice through the REAL Host (plan §15): with the running product's Manual default a
/// confirmed setup PENDS (no trade), and <c>POST /api/signals/{id}/take</c> opens ONE SIMULATED paper trade — persisted
/// to the real Testcontainers Postgres through the real Host DI + the SAME open path the automatic flow uses. A
/// re-take after the trade exists → 409. Proves the endpoint, the command handler, the shared opener, and persistence
/// wire together end-to-end. PAPER-ONLY — no broker/order path anywhere (§6.3).
/// </summary>
[Collection("PaperTradingDb")]
public sealed class TakeSignalEndpointTests : IAsyncLifetime
{
    private readonly PaperTradingDbFixture _fixture;

    public TakeSignalEndpointTests(PaperTradingDbFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // Current-dated: the pending board prunes by AGE against the live Host's real wall clock, so a stale
    // fixed date would be age-expired before the take. ~1 minute ago keeps it well inside MaxPendingMinutes.
    private static readonly DateTimeOffset Confirmed = DateTimeOffset.UtcNow.AddMinutes(-1);

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
    public async Task A_manual_setup_pends_and_a_take_opens_one_trade_then_a_re_take_conflicts()
    {
        // Manual default → a confirmed setup pends; Immediate entry mode → a take opens the trade directly.
        // ExpireOnKillzoneEnd is OFF here: this test verifies the take MECHANICS, not killzone timing, and the
        // live Host uses the real wall clock — so a pending must not be pruned just because the moment the test
        // happens to run is outside an active killzone (killzone-end expiry is unit-tested with a FakeTimeProvider).
        await using var factory = CreateFactory(
            ("Ict:PaperTrading:DefaultEntryMode", "Manual"),
            ("Ict:Execution:Entry:Mode", "Immediate"),
            ("Ict:PaperTrading:Pending:ExpireOnKillzoneEnd", "false"));

        var bus = factory.Services.GetRequiredService<IMessageBus>();
        var setup = BullishSetupDto();

        // 1. Confirm in Manual → it PENDS. No trade is opened.
        await bus.PublishAsync(new SetupConfirmed(setup));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var trades = scope.ServiceProvider.GetRequiredService<IPaperTradeRepository>();
            (await trades.GetOpenAsync()).Should().BeEmpty("a Manual setup opens nothing until taken");
        }

        // 2. TAKE it → 200 with the opened trade DTO.
        using var client = factory.CreateClient();
        var takeResponse = await client.PostAsync($"/api/signals/{setup.Id}/take", content: null);
        takeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await takeResponse.Content.ReadFromJsonAsync<PaperTradeDto>();
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(setup.Id, "the taken trade carries the deterministic seam id");
        dto.Symbol.Should().Be("EURUSD");
        dto.Status.Should().Be(TradeStatus.Open.ToString());

        // The trade was persisted to the real Postgres through the real Host DI.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var trades = scope.ServiceProvider.GetRequiredService<IPaperTradeRepository>();
            (await trades.GetOpenAsync()).Should().ContainSingle("the take opened exactly one persisted trade");
        }

        // 3. A re-take of the same id → 409 (already taken); no second trade.
        var reTake = await client.PostAsync($"/api/signals/{setup.Id}/take", content: null);
        reTake.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var trades = scope.ServiceProvider.GetRequiredService<IPaperTradeRepository>();
            (await trades.GetOpenAsync()).Should().ContainSingle("a re-take must not open a second trade");
        }
    }

    [DockerRequiredFact]
    public async Task Taking_an_unknown_signal_returns_404()
    {
        await using var factory = CreateFactory(("Ict:PaperTrading:DefaultEntryMode", "Manual"));
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/signals/{Guid.NewGuid()}/take", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] overrides) =>
        new HostFactory(_fixture.ConnectionString, overrides);

    /// <summary>Hosts the real <c>Program</c> against the Testcontainers DB with the replay feed OFF (the test drives
    /// the bus + REST directly). Unlike <see cref="HostScanLoopTests"/>, these tests WANT the Manual TAKE workflow, so
    /// the entry-mode override is supplied per test rather than forced to Auto.</summary>
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
