using IctTrader.Domain.Services;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Performance.Application;
using IctTrader.Performance.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Locks WP6 — the Performance module's bus wiring (plan §5.3). It composes the in-memory bus + the Performance
/// module (the same registration the Host uses) in a real <see cref="ServiceCollection"/> so the closed-trade
/// handler + the two query handlers are <c>AddMessaging</c>-scanned exactly as in production. Publishing
/// <see cref="PaperTradeClosed"/> events and then querying proves the candle→…→performance chain serves REAL
/// R-based metrics + a cumulative-R equity curve over the bus — no Postgres needed (the read-model is in-memory).
/// </summary>
public sealed class PerformanceModuleTests
{
    private static readonly DateTimeOffset T0 = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static IServiceProvider BuildModule()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddMessaging(typeof(PaperTradeClosedHandler).Assembly);
        services.AddPerformanceModule();
        return services.BuildServiceProvider();
    }

    private static PaperTradeDto ClosedTrade(decimal realizedR, int minute) => new(
        Id: Guid.NewGuid(),
        SetupId: Guid.NewGuid(),
        Symbol: "EURUSD",
        Direction: "Bullish",
        Status: "Closed",
        Style: "Intraday",
        Killzone: "LondonOpen",
        Entry: 1.0832m,
        Stop: 1.0800m,
        Targets: [1.0876m, 1.0920m],
        Size: 0.31m,
        OpenedAtUtc: T0,
        ClosedAtUtc: T0.AddMinutes(minute),
        RealizedR: realizedR,
        Lifecycle: "Closed",
        CloseReason: "TargetHit",
        NetR: realizedR,
        GrossPnl: realizedR * 100m,
        Costs: 0m,
        NetPnl: realizedR * 100m,
        HasScaledOut: false,
        IsBreakevenArmed: false,
        RiskBudget: 100m,
        Timeframe: "M5",
        CurrentStop: 1.0800m,
        ExitPrice: 1.0920m,
        ManagedFromUtc: T0);

    [Fact]
    public async Task The_closed_trade_handler_and_query_handlers_serve_real_R_based_metrics_over_the_bus()
    {
        var provider = BuildModule();
        var bus = provider.GetRequiredService<IMessageBus>();

        // Drive the same +2R, -1R, +3R, -1R stream the calculator's worked example locks.
        foreach (var (r, minute) in new[] { (2m, 0), (-1m, 5), (3m, 10), (-1m, 15) })
        {
            await bus.PublishAsync(new PaperTradeClosed(ClosedTrade(r, minute), Outcome: "Closed"));
        }

        var summary = await bus.QueryAsync(new GetPerformanceSummaryQuery());

        summary.TradeCount.Should().Be(4);
        summary.WinRate.Should().Be(0.5m);
        summary.AverageR.Should().Be(0.75m);
        summary.ProfitFactor.Should().Be(2.5m);
        summary.Expectancy.Should().Be(0.75m);
        summary.MaxDrawdown.Should().Be(1m); // absolute peak-to-trough in R units
    }

    [Fact]
    public async Task The_equity_curve_query_returns_the_cumulative_R_curve_over_the_bus()
    {
        var provider = BuildModule();
        var bus = provider.GetRequiredService<IMessageBus>();

        foreach (var (r, minute) in new[] { (2m, 0), (-1m, 5), (3m, 10) })
        {
            await bus.PublishAsync(new PaperTradeClosed(ClosedTrade(r, minute), Outcome: "Closed"));
        }

        var curve = await bus.QueryAsync(new GetEquityCurveQuery());

        curve.Select(p => p.Equity).Should().Equal(2m, 1m, 4m); // running ΣR from the zero baseline
        curve.Select(p => p.AtUtc).Should().Equal(T0, T0.AddMinutes(5), T0.AddMinutes(10));
    }

    [Fact]
    public async Task The_closed_trade_handler_records_and_recomputes_so_the_query_reflects_the_appended_trade()
    {
        var provider = BuildModule();
        var bus = provider.GetRequiredService<IMessageBus>();

        // Publishing PaperTradeClosed runs the handler (record → recompute → publish PerformanceUpdated). The publish
        // is awaited, so a subsequent query reflecting the trade proves the whole orchestration ran without throwing.
        await bus.PublishAsync(new PaperTradeClosed(ClosedTrade(2m, 0), Outcome: "Closed"));

        var summary = await bus.QueryAsync(new GetPerformanceSummaryQuery());
        summary.TradeCount.Should().Be(1);
        summary.WinRate.Should().Be(1m);
        summary.ProfitFactor.Should().Be(PerformanceCalculator.UndefinedProfitFactor); // one win, no losses
    }

    [Fact]
    public async Task An_empty_state_serves_the_documented_zero_summary_and_empty_curve()
    {
        var provider = BuildModule();
        var bus = provider.GetRequiredService<IMessageBus>();

        var summary = await bus.QueryAsync(new GetPerformanceSummaryQuery());
        var curve = await bus.QueryAsync(new GetEquityCurveQuery());

        summary.TradeCount.Should().Be(0);
        summary.ProfitFactor.Should().Be(0m); // "n/a — no trades" sentinel
        curve.Should().BeEmpty();
    }
}
