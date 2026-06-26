using FluentAssertions;
using IctTrader.Scanning.Application.Scanning;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks the Scanning recent-setup chart read-model (plan §9.1): the bounded per-symbol overlay store the
/// dashboard's ICT Pattern Chart draws confirmed setups from. Setups return NEWEST-FIRST, respect <c>max</c>,
/// evict past the per-symbol cap, and isolate by symbol. A bus wiring test composes the in-memory bus + the
/// Scanning recent-setup store + handlers, publishes <see cref="SetupConfirmed"/>, and queries
/// <see cref="GetRecentSetupsQuery"/> to prove the overlays serve REAL confirmed setups over the bus.
///
/// <para>Read-only advisory sink (plan §6.3): setups are advisory overlays (<see cref="SetupDto.IsAdvisoryOnly"/>
/// is always true), not orders.</para>
/// </summary>
public sealed class RecentSetupStoreTests
{
    private static readonly DateTimeOffset T0 = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

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

    [Fact]
    public void Recent_returns_setups_newest_first()
    {
        var store = new RecentSetupStore();

        store.Add(Setup("EURUSD", minute: 0));
        store.Add(Setup("EURUSD", minute: 5));
        store.Add(Setup("EURUSD", minute: 10));

        var recent = store.Recent("EURUSD", 10);

        recent.Select(s => s.DetectedAtUtc)
            .Should().Equal(T0.AddMinutes(10), T0.AddMinutes(5), T0);
    }

    [Fact]
    public void Recent_caps_the_window_to_max_newest_first()
    {
        var store = new RecentSetupStore();
        for (var i = 0; i < 5; i++)
        {
            store.Add(Setup("EURUSD", minute: i));
        }

        var recent = store.Recent("EURUSD", 2);

        recent.Select(s => s.DetectedAtUtc).Should().Equal(T0.AddMinutes(4), T0.AddMinutes(3));
    }

    [Fact]
    public void Recent_evicts_the_oldest_past_the_per_symbol_cap()
    {
        var store = new RecentSetupStore();

        var total = RecentSetupStore.MaxSetupsPerSymbol + 20;
        for (var i = 0; i < total; i++)
        {
            store.Add(Setup("EURUSD", minute: i));
        }

        var all = store.Recent("EURUSD", int.MaxValue);
        all.Should().HaveCount(RecentSetupStore.MaxSetupsPerSymbol);

        // Newest-first: the last-added is at the top; the survivors start at the first NON-evicted minute.
        all[0].DetectedAtUtc.Should().Be(T0.AddMinutes(total - 1));
        all[^1].DetectedAtUtc.Should().Be(T0.AddMinutes(total - RecentSetupStore.MaxSetupsPerSymbol));
    }

    [Fact]
    public void Setups_are_isolated_by_symbol()
    {
        var store = new RecentSetupStore();

        store.Add(Setup("EURUSD", minute: 0));
        store.Add(Setup("GBPUSD", minute: 1));

        store.Recent("EURUSD", 10).Should().ContainSingle().Which.Symbol.Should().Be("EURUSD");
        store.Recent("GBPUSD", 10).Should().ContainSingle().Which.Symbol.Should().Be("GBPUSD");
        store.Recent("USDJPY", 10).Should().BeEmpty();
    }

    [Fact]
    public void A_non_positive_max_or_unknown_symbol_returns_an_empty_window()
    {
        var store = new RecentSetupStore();
        store.Add(Setup("EURUSD", minute: 0));

        store.Recent("EURUSD", 0).Should().BeEmpty();
        store.Recent("EURUSD", -1).Should().BeEmpty();
        store.Recent("USDJPY", 10).Should().BeEmpty();
    }

    [Fact]
    public async Task The_recent_setups_query_serves_real_overlays_over_the_bus()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecentSetupStore>();
        services.AddScoped<IEventHandler<SetupConfirmed>, SetupConfirmedChartProjectionHandler>();
        services.AddScoped<IQueryHandler<GetRecentSetupsQuery, IReadOnlyList<SetupDto>>, GetRecentSetupsQueryHandler>();
        services.AddMessaging();
        using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new SetupConfirmed(Setup("EURUSD", minute: 0)));
        await bus.PublishAsync(new SetupConfirmed(Setup("EURUSD", minute: 5)));

        var overlays = await bus.QueryAsync(new GetRecentSetupsQuery("EURUSD", 20));

        // Newest-first, both setups.
        overlays.Select(s => s.DetectedAtUtc).Should().Equal(T0.AddMinutes(5), T0);
    }
}
