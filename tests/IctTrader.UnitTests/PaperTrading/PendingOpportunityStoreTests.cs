using FluentAssertions;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.PaperTrading.Application;
using IctTrader.PaperTrading.Application.Trading;
using IctTrader.Scanning.Contracts;

namespace IctTrader.UnitTests.PaperTrading;

/// <summary>
/// Locks the in-memory pending-opportunity board (the Manual-mode TAKE watchlist, plan §15): add/lookup, expiry by age
/// AND killzone-end, the size bound, and id de-dupe. Pure + clock-free — the caller passes <c>nowUtc</c>, so every
/// expiry assertion is deterministic with no ambient clock.
/// </summary>
public class PendingOpportunityStoreTests
{
    // 07:00 UTC on 2024-07-01 = 03:00 NY = inside the London Open killzone (NY is UTC-4 in July).
    private static readonly DateTimeOffset InKillzone = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    // 13:00 UTC on 2024-07-01 = 09:00 NY = inside New York Open. 17:00 UTC = 13:00 NY = the PM (not an FX hunt killzone).
    private static readonly DateTimeOffset OutsideKillzone = new(2024, 7, 1, 17, 30, 0, TimeSpan.Zero); // 13:30 NY = Pm

    private static PendingOpportunityStore CreateStore(
        int maxPendingMinutes = 240, int maxPending = 50, bool expireOnKillzoneEnd = true)
    {
        var options = new PendingOpportunityOptions
        {
            MaxPendingMinutes = maxPendingMinutes,
            MaxPending = maxPending,
            ExpireOnKillzoneEnd = expireOnKillzoneEnd,
        };
        var killzoneClock = new KillzoneClock(
            new NyClock(TimeProvider.System), KillzoneSchedule.CreateDefault());
        // The London Open + New York Open hunt-set (the ICT default), so InKillzone is active and OutsideKillzone is not.
        return new PendingOpportunityStore(
            options, killzoneClock, [Killzone.LondonOpen, Killzone.NewYorkOpen]);
    }

    private static PendingOpportunity Pending(DateTimeOffset detectedAt, Guid? id = null) =>
        new(SetupDto(detectedAt, id), InstrumentClass.Fx);

    private static SetupDto SetupDto(DateTimeOffset detectedAt, Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
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
        DetectedAtUtc: detectedAt,
        IsAdvisoryOnly: true);

    [Fact]
    public void Adds_and_takes_a_pending_exactly_once()
    {
        var store = CreateStore();
        var pending = Pending(InKillzone);

        store.Add(pending, InKillzone);
        store.IsPending(pending.Id, InKillzone).Should().BeTrue();

        var taken = store.TryTake(pending.Id, InKillzone);
        taken.Should().NotBeNull();
        taken!.Id.Should().Be(pending.Id);

        // The take consumed it — a second take is a miss, and it is no longer pending.
        store.TryTake(pending.Id, InKillzone).Should().BeNull();
        store.IsPending(pending.Id, InKillzone).Should().BeFalse();
    }

    [Fact]
    public void Take_of_an_unknown_id_is_a_miss()
    {
        var store = CreateStore();

        store.TryTake(Guid.NewGuid(), InKillzone).Should().BeNull();
    }

    [Fact]
    public void De_dupes_by_deterministic_id_replacing_in_place()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();

        // The SAME id added twice (a redelivered candle re-confirming the same setup) must NOT duplicate the board.
        store.Add(Pending(InKillzone, id), InKillzone);
        store.Add(Pending(InKillzone, id), InKillzone);

        store.Count(InKillzone).Should().Be(1);
        store.TryTake(id, InKillzone).Should().NotBeNull();
        store.TryTake(id, InKillzone).Should().BeNull("the single de-duped entry was consumed");
    }

    [Fact]
    public void Expires_by_age_past_the_max_window()
    {
        var store = CreateStore(maxPendingMinutes: 60);
        var pending = Pending(InKillzone);
        store.Add(pending, InKillzone);

        // Still inside the killzone but 61 minutes later → aged out.
        var later = InKillzone.AddMinutes(61);
        store.IsPending(pending.Id, later).Should().BeFalse("it aged out past the 60-minute window");
        store.TryTake(pending.Id, later).Should().BeNull();
    }

    [Fact]
    public void Does_not_expire_by_age_within_the_max_window()
    {
        var store = CreateStore(maxPendingMinutes: 60);
        var pending = Pending(InKillzone);
        store.Add(pending, InKillzone);

        var within = InKillzone.AddMinutes(30); // still inside the killzone AND inside the age window
        store.IsPending(pending.Id, within).Should().BeTrue();
    }

    [Fact]
    public void Expires_when_the_killzone_entry_window_has_ended()
    {
        // A long age window so ONLY killzone-end can expire it.
        var store = CreateStore(maxPendingMinutes: 1_000, expireOnKillzoneEnd: true);
        var pending = Pending(InKillzone);
        store.Add(pending, InKillzone);

        // "now" has moved to 13:30 NY (the Pm session) — no longer an active entry for the London/NY hunt-set.
        store.IsPending(pending.Id, OutsideKillzone).Should().BeFalse("the killzone-entry window has ended");
        store.TryTake(pending.Id, OutsideKillzone).Should().BeNull();
    }

    [Fact]
    public void Does_not_expire_on_killzone_end_when_the_policy_is_off()
    {
        // Killzone-end expiry disabled (e.g. a backtest with no live session math) — only age expires it.
        var store = CreateStore(maxPendingMinutes: 1_000, expireOnKillzoneEnd: false);
        var pending = Pending(InKillzone);
        store.Add(pending, InKillzone);

        store.IsPending(pending.Id, OutsideKillzone).Should().BeTrue("killzone-end expiry is off, age is not reached");
    }

    [Fact]
    public void Evicts_the_oldest_when_over_the_size_cap()
    {
        var store = CreateStore(maxPending: 2);

        var oldest = Pending(InKillzone, Guid.NewGuid());
        var middle = Pending(InKillzone.AddMinutes(1), Guid.NewGuid());
        var newest = Pending(InKillzone.AddMinutes(2), Guid.NewGuid());

        store.Add(oldest, InKillzone);
        store.Add(middle, InKillzone);
        store.Add(newest, InKillzone); // exceeds the cap of 2 → the oldest is evicted

        store.Count(InKillzone).Should().Be(2);
        store.IsPending(oldest.Id, InKillzone).Should().BeFalse("the oldest by detection time was evicted");
        store.IsPending(middle.Id, InKillzone).Should().BeTrue();
        store.IsPending(newest.Id, InKillzone).Should().BeTrue();
    }

    [Fact]
    public void TryTake_distinguishes_an_expired_pending_from_a_never_known_id()
    {
        var store = CreateStore(maxPendingMinutes: 60);
        var pending = Pending(InKillzone);
        store.Add(pending, InKillzone);

        // Present but aged out → Expired (so the endpoint can answer 409, not a blanket 404).
        var later = InKillzone.AddMinutes(61);
        store.TryTake(pending.Id, later, out var expiredMiss).Should().BeNull();
        expiredMiss.Should().Be(PendingOpportunityStore.TakeMiss.Expired);

        // Never on the board → NotFound (404).
        store.TryTake(Guid.NewGuid(), InKillzone, out var unknownMiss).Should().BeNull();
        unknownMiss.Should().Be(PendingOpportunityStore.TakeMiss.NotFound);

        // A live take still succeeds (and reports no miss).
        var fresh = Pending(InKillzone);
        store.Add(fresh, InKillzone);
        store.TryTake(fresh.Id, InKillzone, out _).Should().NotBeNull();
    }

    [Fact]
    public void AgeExpiry_reports_detection_plus_window_when_pending_and_null_otherwise()
    {
        var store = CreateStore(maxPendingMinutes: 60);
        var pending = Pending(InKillzone);
        store.Add(pending, InKillzone);

        store.AgeExpiryFor(pending.Id, InKillzone).Should().Be(InKillzone.AddMinutes(60));
        store.AgeExpiryFor(Guid.NewGuid(), InKillzone).Should().BeNull("an unknown id has no pending expiry");
    }
}
