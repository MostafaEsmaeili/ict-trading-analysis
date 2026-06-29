using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Scanning.Application.Signals;
using IctTrader.Scanning.Contracts;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks the semi-auto TAKE-state enrichment of the ranked signals feed (plan §15): <see cref="SignalRankingService"/>
/// projects each <see cref="ISignalTakeStateProvider"/> result onto the optional <see cref="RankedSignalDto"/> tail, and
/// the no-op default leaves every signal in its takeable-unknown wire default (so an existing consumer is unaffected).
/// </summary>
public sealed class SignalRankingTakeStateTests
{
    private static readonly DateTimeOffset Now = new(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static SetupDto Setup() => new(
        Id: Guid.NewGuid(),
        Symbol: "EURUSD",
        Direction: "Bullish",
        Killzone: "LondonOpen",
        Style: "Intraday",
        Grade: "B",
        TriggerTimeframe: "M5",
        Entry: 1.0832m,
        Stop: 1.0800m,
        Targets: [1.0876m, 1.0920m],
        RewardRatio: 2.5m,
        Reason: "sweep -> MSS -> OTE",
        DetectedAtUtc: Now.AddMinutes(-5),
        IsAdvisoryOnly: true,
        Score: 70);

    private static (SignalRankingService Service, SignalFeedStore Store) Build(ISignalTakeStateProvider? provider)
    {
        var opts = new SignalRankingOptions();
        var store = new SignalFeedStore(opts);
        return (new SignalRankingService(store, new SignalRanker(opts), opts, provider), store);
    }

    [Fact]
    public void Without_a_provider_the_signal_carries_the_takeable_unknown_wire_default()
    {
        var (service, store) = Build(provider: null);
        store.Add(Setup(), Now);

        var signal = service.Top(Now).Single();

        signal.EntryMode.Should().Be("Auto");
        signal.IsTaken.Should().BeFalse();
        signal.BlockReason.Should().BeNull();
        signal.ExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public void With_a_provider_the_take_state_is_projected_onto_the_signal()
    {
        var expiry = Now.AddMinutes(60).ToString("O");
        var provider = new StubProvider(new SignalTakeState("Manual", IsTaken: false, BlockReason: null, ExpiresAtUtc: expiry));
        var (service, store) = Build(provider);
        store.Add(Setup(), Now);

        var signal = service.Top(Now).Single();

        signal.EntryMode.Should().Be("Manual");
        signal.IsTaken.Should().BeFalse();
        signal.BlockReason.Should().BeNull("a live Manual pending is takeable");
        signal.ExpiresAtUtc.Should().Be(expiry);
    }

    [Fact]
    public void A_taken_signal_is_marked_taken_with_a_block_reason()
    {
        var provider = new StubProvider(new SignalTakeState("Manual", IsTaken: true, BlockReason: "AlreadyTaken", ExpiresAtUtc: null));
        var (service, store) = Build(provider);
        store.Add(Setup(), Now);

        var signal = service.Top(Now).Single();

        signal.IsTaken.Should().BeTrue();
        signal.BlockReason.Should().Be("AlreadyTaken");
    }

    [Fact]
    public void Signals_that_tie_on_every_rank_key_are_ordered_deterministically_by_id()
    {
        // Three setups identical on grade/score/RR/TF/DetectedAtUtc (a perfect tie) but distinct ids, ADDED in reverse
        // id order. The ranked feed must be a TOTAL order — stable by the deterministic SetupDto.Id — regardless of the
        // store's enumeration order (otherwise equal-priority signals' relative rank would be non-deterministic).
        var (service, store) = Build(provider: null);
        var a = new Guid("00000000-0000-0000-0000-000000000001");
        var b = new Guid("00000000-0000-0000-0000-000000000002");
        var c = new Guid("00000000-0000-0000-0000-000000000003");
        store.Add(Setup() with { Id = c }, Now);
        store.Add(Setup() with { Id = b }, Now);
        store.Add(Setup() with { Id = a }, Now);

        var ranked = service.Top(Now);

        ranked.Select(r => r.Setup.Id).Should().Equal(a, b, c);
        ranked.Select(r => r.Rank).Should().Equal(1, 2, 3); // ranks stamped in that stable order
    }

    private sealed class StubProvider(SignalTakeState state) : ISignalTakeStateProvider
    {
        public SignalTakeState DescribeFor(SetupDto setup, DateTimeOffset nowUtc) => state;
    }
}
