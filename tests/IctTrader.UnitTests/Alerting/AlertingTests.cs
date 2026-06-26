using FluentAssertions;
using IctTrader.Alerting.Application;
using IctTrader.Alerting.Contracts;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.UnitTests.Alerting;

/// <summary>
/// Locks the Alerting module's bus wiring (plan §9). It composes the in-memory bus + the Alerting module (the
/// same registration the Host uses) in a real <see cref="ServiceCollection"/> so the setup/trade alert handlers
/// and the recent-alerts query handler are <c>AddMessaging</c>-scanned exactly as in production. Publishing
/// <see cref="SetupConfirmed"/> / <see cref="PaperTradeClosed"/> events and then querying proves the dashboard's
/// Alerts feed serves REAL setup/trade notifications over the bus — and that the bounded buffer evicts the oldest.
///
/// <para>Read-only advisory sink (plan §6.3 guardrail): the alerts are notifications, not orders.</para>
/// </summary>
public sealed class AlertingTests
{
    private static readonly DateTimeOffset T0 = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static IServiceProvider BuildModule()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddMessaging(typeof(SetupConfirmedAlertHandler).Assembly);
        services.AddAlertingModule();
        return services.BuildServiceProvider();
    }

    private static SetupDto Setup(string symbol, int minute) => new(
        Id: Guid.NewGuid(),
        Symbol: symbol,
        Direction: "Bullish",
        Killzone: "LondonOpen",
        Style: "Intraday",
        Grade: "B",
        TriggerTimeframe: "M5",
        Entry: 1.0832m,
        Stop: 1.0800m,
        Targets: [1.0876m, 1.0920m],
        RewardRatio: 2.5m,
        Reason: "Bullish sweep -> MSS -> OTE entry",
        DetectedAtUtc: T0.AddMinutes(minute),
        IsAdvisoryOnly: true);

    private static PaperTradeDto ClosedTrade(string symbol, decimal realizedR, int minute) => new(
        Id: Guid.NewGuid(),
        SetupId: Guid.NewGuid(),
        Symbol: symbol,
        Direction: "Bullish",
        Status: "Closed",
        Style: "Intraday",
        Killzone: "LondonOpen",
        Entry: 1.0832m,
        Stop: 1.0800m,
        Targets: [1.0876m, 1.0920m],
        Size: 0.31m,
        OpenedAtUtc: T0.AddMinutes(minute),
        ClosedAtUtc: T0.AddMinutes(minute + 30),
        RealizedR: realizedR);

    [Fact]
    public async Task The_alert_handlers_record_setup_and_trade_events_newest_first_over_the_bus()
    {
        var provider = BuildModule();
        var bus = provider.GetRequiredService<IMessageBus>();

        // Setup confirms first, then the trade it produced closes — so the trade-closed alert is the NEWEST.
        var setup = Setup("EURUSD", minute: 0);
        await bus.PublishAsync(new SetupConfirmed(setup));
        await bus.PublishAsync(new PaperTradeClosed(ClosedTrade("EURUSD", realizedR: 2m, minute: 5), Outcome: "TargetHit"));

        var alerts = await bus.QueryAsync(new GetRecentAlertsQuery(10));

        alerts.Should().HaveCount(2);

        // Newest-first: the trade-closed alert sits at the top.
        var closed = alerts[0];
        closed.Kind.Should().Be("TradeClosed");
        closed.Symbol.Should().Be("EURUSD");
        closed.Message.Should().Be("Closed EURUSD TargetHit (+2.00R)");
        closed.Direction.Should().Be("Bullish");

        var confirmed = alerts[1];
        confirmed.Kind.Should().Be("Setup");
        confirmed.Symbol.Should().Be("EURUSD");
        confirmed.Message.Should().Be(setup.Reason); // the §2.5 reasoning, verbatim
        confirmed.Direction.Should().Be("Bullish");
        confirmed.Killzone.Should().Be("LondonOpen");
        confirmed.Style.Should().Be("Intraday");
        confirmed.AtUtc.Should().Be(setup.DetectedAtUtc);
    }

    [Fact]
    public async Task A_losing_trade_close_formats_the_signed_R_negative()
    {
        var provider = BuildModule();
        var bus = provider.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new PaperTradeClosed(ClosedTrade("GBPUSD", realizedR: -1m, minute: 0), Outcome: "StopHit"));

        var alerts = await bus.QueryAsync(new GetRecentAlertsQuery(10));

        alerts.Should().ContainSingle();
        alerts[0].Message.Should().Be("Closed GBPUSD StopHit (-1.00R)");
    }

    [Fact]
    public async Task The_recent_query_caps_the_returned_window_to_max_newest_first()
    {
        var provider = BuildModule();
        var bus = provider.GetRequiredService<IMessageBus>();

        for (var i = 0; i < 5; i++)
        {
            await bus.PublishAsync(new SetupConfirmed(Setup($"SYM{i}", minute: i)));
        }

        var alerts = await bus.QueryAsync(new GetRecentAlertsQuery(2));

        // Only the two most-recent, newest-first.
        alerts.Select(a => a.Symbol).Should().Equal("SYM4", "SYM3");
    }

    [Fact]
    public void The_ring_buffer_evicts_the_oldest_past_the_cap()
    {
        var log = new AlertLog();

        // Fill past the cap; each alert is tagged with its sequence number in the Symbol so we can see eviction.
        var total = AlertLog.MaxAlerts + 50;
        for (var i = 0; i < total; i++)
        {
            log.Add(new AlertDto(
                Id: Guid.NewGuid(),
                Kind: "Setup",
                Symbol: $"SYM{i}",
                Message: "msg",
                Direction: null,
                Killzone: null,
                Style: null,
                AtUtc: T0.AddSeconds(i)));
        }

        // Asking for more than the cap returns at most the cap — the oldest 50 were evicted.
        var all = log.Recent(int.MaxValue);
        all.Should().HaveCount(AlertLog.MaxAlerts);

        // Newest-first: the last-added is at the top; the survivors start at the first NON-evicted index.
        all[0].Symbol.Should().Be($"SYM{total - 1}");
        all[^1].Symbol.Should().Be($"SYM{total - AlertLog.MaxAlerts}");
    }

    [Fact]
    public void A_non_positive_max_returns_an_empty_window()
    {
        var log = new AlertLog();
        log.Add(new AlertDto(Guid.NewGuid(), "Setup", "EURUSD", "msg", null, null, null, T0));

        log.Recent(0).Should().BeEmpty();
        log.Recent(-1).Should().BeEmpty();
    }
}
