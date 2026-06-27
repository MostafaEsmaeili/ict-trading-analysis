using IctTrader.Alerting.Contracts;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.E2E.Fixtures;
using IctTrader.E2E.Hooks;
using IctTrader.MarketData.Contracts;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Performance.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace IctTrader.E2E.StepDefinitions;

/// <summary>
/// The driver for the paper-trade pipeline scenarios (plan §8.2/§8.3). Every step goes through the REAL Host's
/// in-memory bus into the REAL module handlers + EF persistence on the Testcontainers Postgres — a confirmed
/// advisory <see cref="SetupConfirmed"/> opens a paper trade (PaperTrading), a <see cref="CandleIngested"/> drives
/// the management pass to a close, and the assertions read back through the bus query handlers
/// (<see cref="GetPerformanceSummaryQuery"/>, <see cref="GetRecentAlertsQuery"/>) + the aggregate repositories.
/// This exercises Scanning's confirmation seam, PaperTrading, Performance, and Alerting end-to-end on the bus.
/// </summary>
[Binding]
public sealed class PaperTradePipelineSteps(PipelineWorld world)
{
    private readonly PipelineWorld _world = world ?? throw new ArgumentNullException(nameof(world));

    private IMessageBus Bus => _world.RequireFactory().Services.GetRequiredService<IMessageBus>();

    // ── Background ────────────────────────────────────────────────────────────────────────────────────────

    [Given("a clean trading database")]
    public static void GivenACleanTradingDatabase()
    {
        // Respawn truncated the schema in the BeforeScenario hook; this step documents the precondition in the
        // ubiquitous language (the slate is clean before the pipeline runs).
    }

    [Given("the real Host is booted over Testcontainers Postgres")]
    public void GivenTheRealHostIsBooted()
    {
        _world.Factory = new CustomWebApplicationFactory(
            PipelineHooks.Postgres.ConnectionString,
            CandleFixtures.SessionStartUtc);
    }

    [Given("the symbol \"(.*)\" is being analysed")]
    public void GivenTheSymbolIsBeingAnalysed(string symbol)
    {
        _world.Symbol = symbol;
    }

    [Given("the market clock is anchored to New York time")]
    public void GivenTheMarketClockIsAnchoredToNewYorkTime()
    {
        // The Host's TimeProvider is the FakeTimeProvider the factory anchored to the London-Open instant; the
        // fixture candles carry their own NY-anchored UTC open times, so the pipeline is fully deterministic.
        _world.RequireFactory();
    }

    // ── Given: a confirmed setup ──────────────────────────────────────────────────────────────────────────

    [Given("a confirmed bullish London-killzone setup from the Asian-sweep displacement model")]
    public void GivenAConfirmedBullishLondonSetup()
    {
        _world.ConfirmedSetup = CandleFixtures.BullishLondonSetup();
    }

    // ── When: drive the bus ───────────────────────────────────────────────────────────────────────────────

    [When("the setup is confirmed on the bus")]
    public async Task WhenTheSetupIsConfirmedOnTheBus()
    {
        var setup = _world.ConfirmedSetup
            ?? throw new InvalidOperationException("No setup was prepared for the scenario.");
        await Bus.PublishAsync(new SetupConfirmed(setup));
    }

    [When("a candle trades up through the draw on liquidity")]
    public async Task WhenACandleTradesUpThroughTheDraw()
    {
        await Bus.PublishAsync(new CandleIngested(CandleFixtures.RunnerToTargetCandle()));
    }

    [When("a candle wicks below the protective stop")]
    public async Task WhenACandleWicksBelowTheStop()
    {
        await Bus.PublishAsync(new CandleIngested(CandleFixtures.StopOutCandle()));
    }

    // ── Then: assert through the real read sides ──────────────────────────────────────────────────────────

    [Then("a paper trade should be opened for \"(.*)\"")]
    public async Task ThenAPaperTradeShouldBeOpenedFor(string symbol)
    {
        var active = await Bus.QueryAsync(new GetActiveTradesQuery());
        active.Should().ContainSingle("the confirmed advisory setup opened exactly one paper trade");
        var trade = active.Single();
        trade.Symbol.Should().Be(symbol);
        trade.Status.Should().Be(TradeStatus.Open.ToString());
        trade.Direction.Should().Be(Direction.Bullish.ToString());

        // Capture the trade id so a later step can reload it once it leaves the open set (it closes + settles).
        _world.OpenedTradeId = trade.Id;
    }

    [Then("the open trade should be advisory only with no live-order path")]
    public async Task ThenTheOpenTradeShouldBeAdvisoryOnly()
    {
        // The setup that produced the trade is structurally advisory-only (plan §6.3 guardrail).
        _world.ConfirmedSetup!.IsAdvisoryOnly.Should().BeTrue();

        // The frozen wire DTO carries no order/execute field — there is structurally nowhere for an order to go.
        var dto = (await Bus.QueryAsync(new GetActiveTradesQuery())).Single();
        typeof(PaperTradeDto).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(
                name => name.Contains("Order", StringComparison.OrdinalIgnoreCase)
                     || name.Contains("Broker", StringComparison.OrdinalIgnoreCase)
                     || name.Contains("Execute", StringComparison.OrdinalIgnoreCase),
                "a paper trade routes nowhere near a live order (the NON-NEGOTIABLE guardrail)");
        dto.Id.Should().NotBeEmpty();
    }

    [Then("the paper trade should close with outcome \"(.*)\"")]
    public async Task ThenThePaperTradeShouldCloseWithOutcome(string outcome)
    {
        // It is no longer in the open set.
        (await Bus.QueryAsync(new GetActiveTradesQuery())).Should()
            .BeEmpty("the managed candle closed and settled the trade");

        // It is persisted as Closed with the expected close reason (read straight from the repository).
        var trade = await SingleSettledTradeAsync();
        trade.Status.Should().Be(TradeStatus.Closed);
        trade.CloseReason.Should().Be(Enum.Parse<TradeCloseReason>(outcome));
    }

    [Then("the closed trade should realise minus one R")]
    public async Task ThenTheClosedTradeShouldRealiseMinusOneR()
    {
        var trade = await SingleSettledTradeAsync();
        trade.RealizedR!.Value.Should().BeApproximately(-1m, 0.0001m, "a stop-out books exactly the frozen -1R");
    }

    [Then("the performance summary should show a win rate of (.*) percent over (.*) trade")]
    public async Task ThenThePerformanceSummaryShows(int winRatePercent, int tradeCount)
    {
        var summary = await Bus.QueryAsync(new GetPerformanceSummaryQuery());
        summary.TradeCount.Should().Be(tradeCount);
        summary.WinRate.Should().Be(winRatePercent / 100m);
    }

    [Then("an advisory alert should record the confirmed setup")]
    public async Task ThenAnAdvisoryAlertRecordsTheSetup()
    {
        var alerts = await Bus.QueryAsync(new GetRecentAlertsQuery(50));
        alerts.Should().Contain(
            a => a.Kind == "Setup" && a.Symbol == IctAnchors.Symbol && a.Direction == Direction.Bullish.ToString(),
            "the Alerting module surfaced the confirmed advisory setup");
    }

    [Then("an advisory alert should record the closed trade")]
    public async Task ThenAnAdvisoryAlertRecordsTheClosedTrade()
    {
        var alerts = await Bus.QueryAsync(new GetRecentAlertsQuery(50));
        alerts.Should().Contain(
            a => a.Kind == "TradeClosed" && a.Symbol == IctAnchors.Symbol,
            "the Alerting module surfaced the closed paper trade");
    }

    private async Task<Domain.Trading.PaperTrade> SingleSettledTradeAsync()
    {
        _world.OpenedTradeId.Should().NotBeEmpty("the trade id was captured when the trade opened");

        await using var scope = _world.RequireFactory().Services.CreateAsyncScope();
        var trades = scope.ServiceProvider.GetRequiredService<IPaperTradeRepository>();
        var trade = await trades.GetByIdAsync(_world.OpenedTradeId);
        return trade ?? throw new InvalidOperationException("The expected settled trade was not found in Postgres.");
    }
}
