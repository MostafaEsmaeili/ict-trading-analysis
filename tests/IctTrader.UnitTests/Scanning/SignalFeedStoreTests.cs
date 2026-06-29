using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Scanning.Application.Signals;
using IctTrader.Scanning.Contracts;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks the cross-matrix signals feed store (plan §9): it de-dupes a redelivered <see cref="SetupDto.Id"/>, drops
/// entries older than the recency cutoff, and never holds more than the configured cap (evicting the oldest).
/// </summary>
public sealed class SignalFeedStoreTests
{
    private static readonly DateTimeOffset Now = new(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static SetupDto Setup(Guid id, string symbol, int detectedMinutesAgo, int score = 70, string grade = "B")
        => new(
            Id: id,
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
            DetectedAtUtc: Now.AddMinutes(-detectedMinutesAgo),
            IsAdvisoryOnly: true,
            Score: score);

    [Fact]
    public void Snapshot_returns_added_live_setups()
    {
        var store = new SignalFeedStore(new SignalRankingOptions());

        store.Add(Setup(Guid.NewGuid(), "EURUSD", detectedMinutesAgo: 5), Now);
        store.Add(Setup(Guid.NewGuid(), "GBPUSD", detectedMinutesAgo: 5), Now);

        store.Snapshot(Now).Should().HaveCount(2);
    }

    [Fact]
    public void A_redelivered_id_de_dupes_in_place()
    {
        var store = new SignalFeedStore(new SignalRankingOptions());
        var id = Guid.NewGuid();

        store.Add(Setup(id, "EURUSD", detectedMinutesAgo: 5, score: 70), Now);
        // Same id (a replayed candle re-confirming the SAME setup) — replaces, not duplicates.
        store.Add(Setup(id, "EURUSD", detectedMinutesAgo: 5, score: 88), Now);

        store.Snapshot(Now).Should().ContainSingle().Which.Score.Should().Be(88);
    }

    [Fact]
    public void A_setup_older_than_the_recency_cutoff_is_dropped()
    {
        var options = new SignalRankingOptions { RecencyCutoffMinutes = 60 };
        var store = new SignalFeedStore(options);

        store.Add(Setup(Guid.NewGuid(), "EURUSD", detectedMinutesAgo: 30), Now);   // live
        store.Add(Setup(Guid.NewGuid(), "GBPUSD", detectedMinutesAgo: 120), Now);  // stale (> 60m)

        var snapshot = store.Snapshot(Now);
        snapshot.Should().ContainSingle().Which.Symbol.Should().Be("EURUSD");
    }

    [Fact]
    public void A_setup_exactly_at_the_recency_boundary_stays_live()
    {
        var options = new SignalRankingOptions { RecencyCutoffMinutes = 60 };
        var store = new SignalFeedStore(options);

        store.Add(Setup(Guid.NewGuid(), "EURUSD", detectedMinutesAgo: 60), Now); // == cutoff, inclusive

        store.Snapshot(Now).Should().ContainSingle();
    }

    [Fact]
    public void Adding_prunes_an_entry_that_became_stale_relative_to_the_new_now()
    {
        var options = new SignalRankingOptions { RecencyCutoffMinutes = 60 };
        var store = new SignalFeedStore(options);

        var early = new DateTimeOffset(2024, 7, 1, 10, 0, 0, TimeSpan.Zero);
        store.Add(Setup(Guid.NewGuid(), "EURUSD", detectedMinutesAgo: 0, score: 70) with { DetectedAtUtc = early }, early);

        // Two hours later a NEW setup arrives — the first is now stale and pruned on Add.
        var later = early.AddHours(2);
        store.Add(Setup(Guid.NewGuid(), "GBPUSD", detectedMinutesAgo: 0) with { DetectedAtUtc = later }, later);

        store.Snapshot(later).Should().ContainSingle().Which.Symbol.Should().Be("GBPUSD");
    }

    [Fact]
    public void The_store_never_exceeds_the_max_feed_size_evicting_the_oldest()
    {
        var options = new SignalRankingOptions { MaxFeedSize = 3, RecencyCutoffMinutes = 100_000 };
        var store = new SignalFeedStore(options);

        // Add 5 setups; minute-ago 5 is the OLDEST, 1 the newest. Cap 3 keeps the three newest.
        for (var minutesAgo = 5; minutesAgo >= 1; minutesAgo--)
        {
            store.Add(Setup(Guid.NewGuid(), $"SYM{minutesAgo}", detectedMinutesAgo: minutesAgo), Now);
        }

        var snapshot = store.Snapshot(Now);
        snapshot.Should().HaveCount(3);
        snapshot.Select(s => s.Symbol).Should().BeEquivalentTo(["SYM1", "SYM2", "SYM3"]);
        snapshot.Select(s => s.Symbol).Should().NotContain(["SYM4", "SYM5"]); // the two oldest evicted
    }
}
