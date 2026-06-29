using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Scanning.Application.Signals;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks the signals feed sink (plan §9): <see cref="SetupConfirmedSignalFeedHandler"/> adds each confirmed advisory
/// setup to the feed store and publishes a <see cref="SignalsUpdated"/> carrying the recomputed ranked top-N. A bus
/// wiring test composes the in-memory bus + the signals services + the handler + a capturing
/// <see cref="SignalsUpdated"/> sink and proves the live push fans out alongside the other SetupConfirmed consumers.
/// </summary>
public sealed class SetupConfirmedSignalFeedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static SetupDto Setup(string symbol, int score, string grade = "B", int minutesAgo = 5)
        => new(
            Id: Guid.NewGuid(),
            Symbol: symbol,
            Direction: "Bullish",
            Killzone: "LondonOpen",
            Style: "Intraday",
            Grade: grade,
            TriggerTimeframe: "M5",
            Entry: 1.0832m,
            Stop: 1.0800m,
            Targets: [1.0876m, 1.0920m],
            RewardRatio: 2.5m,
            Reason: "sweep -> MSS -> OTE",
            DetectedAtUtc: Now.AddMinutes(-minutesAgo),
            IsAdvisoryOnly: true,
            Score: score);

    [Fact]
    public async Task It_adds_to_the_feed_and_publishes_SignalsUpdated_with_the_ranked_top_n()
    {
        var sink = new CapturedSignals();
        using var provider = BuildHost(sink, new SignalRankingOptions());
        var bus = provider.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new SetupConfirmed(Setup("EURUSD", score: 70)));
        await bus.PublishAsync(new SetupConfirmed(Setup("GBPUSD", score: 90)));

        // The feed store now holds both, and the LAST SignalsUpdated carries the ranked top-N (GBPUSD first by score).
        sink.LastTop.Should().NotBeNull();
        sink.LastTop!.Select(r => r.Setup.Symbol).Should().Equal("GBPUSD", "EURUSD");
        sink.LastTop[0].Rank.Should().Be(1);
        sink.LastTop[0].Score.Should().Be(90);

        // The query handler reads the SAME store — proving the sink and the read-side share the feed.
        var ranked = await bus.QueryAsync(new GetSignalsQuery());
        ranked.Select(r => r.Setup.Symbol).Should().Equal("GBPUSD", "EURUSD");
    }

    [Fact]
    public async Task The_pushed_top_n_honours_the_configured_max_feed_size()
    {
        var sink = new CapturedSignals();
        using var provider = BuildHost(sink, new SignalRankingOptions { MaxFeedSize = 1 });
        var bus = provider.GetRequiredService<IMessageBus>();

        // EURUSD is OLDER (10m ago); GBPUSD newer (1m ago). The cap of 1 evicts the oldest by detection time, so the
        // recency-bounded store keeps GBPUSD (the store caps by recency — quality ranking is the ranker's job).
        await bus.PublishAsync(new SetupConfirmed(Setup("EURUSD", score: 70, minutesAgo: 10)));
        await bus.PublishAsync(new SetupConfirmed(Setup("GBPUSD", score: 90, minutesAgo: 1)));

        sink.LastTop.Should().ContainSingle().Which.Setup.Symbol.Should().Be("GBPUSD");
    }

    private static ServiceProvider BuildHost(CapturedSignals sink, SignalRankingOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddSingleton(sink);

        // The signals feed services (mirroring AddScanningModule's wiring, here with explicit options).
        services.AddSingleton(new SignalFeedStore(options));
        services.AddSingleton(new SignalRanker(options));
        services.AddSingleton(sp => new SignalRankingService(
            sp.GetRequiredService<SignalFeedStore>(), sp.GetRequiredService<SignalRanker>(), options));

        // The production handlers + a capturing SignalsUpdated sink.
        services.AddScoped<IEventHandler<SetupConfirmed>, SetupConfirmedSignalFeedHandler>();
        services.AddScoped<IQueryHandler<GetSignalsQuery, IReadOnlyList<RankedSignalDto>>, GetSignalsQueryHandler>();
        services.AddScoped<IEventHandler<SignalsUpdated>, CapturingSignalsHandler>();
        services.AddMessaging();

        return services.BuildServiceProvider();
    }

    private sealed class CapturedSignals
    {
        public IReadOnlyList<RankedSignalDto>? LastTop { get; set; }
    }

    private sealed class CapturingSignalsHandler(CapturedSignals captured) : IEventHandler<SignalsUpdated>
    {
        public Task HandleAsync(SignalsUpdated @event, CancellationToken cancellationToken = default)
        {
            captured.LastTop = @event.Top;
            return Task.CompletedTask;
        }
    }
}
