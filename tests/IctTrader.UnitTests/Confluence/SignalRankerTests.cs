using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Confluence;

/// <summary>
/// Locks the pure-domain <see cref="SignalRanker"/> total order for the "best opportunities" feed: grade beats score
/// beats reward-to-risk beats timeframe priority beats recency. Each tie-break is proven to ONLY apply when every
/// higher key ties, and the ordering is deterministic regardless of input order.
/// </summary>
public sealed class SignalRankerTests
{
    private static readonly DateTimeOffset T0 = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static readonly SignalRanker Ranker = new(new SignalRankingOptions());

    private static RankableSignal<string> Signal(
        string id,
        SetupGrade grade = SetupGrade.B,
        int score = 70,
        decimal rr = 2.5m,
        Timeframe timeframe = Timeframe.M5,
        int minute = 0)
        => new(grade, score, rr, timeframe, T0.AddMinutes(minute), id);

    private static IReadOnlyList<string> Order(params RankableSignal<string>[] signals)
        => Ranker.Rank(signals).Select(s => s.Payload).ToList();

    [Fact]
    public void Grade_is_the_primary_key_A_beats_B_beats_C()
    {
        // A LOW-score A still outranks a HIGH-score B/C: grade dominates everything below it.
        var order = Order(
            Signal("c", grade: SetupGrade.C, score: 100, rr: 9m, timeframe: Timeframe.D1, minute: 100),
            Signal("b", grade: SetupGrade.B, score: 99, rr: 8m, timeframe: Timeframe.H4, minute: 90),
            Signal("a", grade: SetupGrade.A, score: 50, rr: 2m, timeframe: Timeframe.M1, minute: 0));

        order.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Score_breaks_a_grade_tie_higher_first()
    {
        // Same grade — the higher score wins even with a worse RR / older / lower TF.
        var order = Order(
            Signal("low", grade: SetupGrade.B, score: 65, rr: 9m, timeframe: Timeframe.H4, minute: 100),
            Signal("high", grade: SetupGrade.B, score: 90, rr: 2m, timeframe: Timeframe.M1, minute: 0));

        order.Should().Equal("high", "low");
    }

    [Fact]
    public void Reward_ratio_breaks_a_grade_and_score_tie_bigger_first()
    {
        var order = Order(
            Signal("small", grade: SetupGrade.B, score: 70, rr: 2.0m, timeframe: Timeframe.H4, minute: 100),
            Signal("big", grade: SetupGrade.B, score: 70, rr: 5.0m, timeframe: Timeframe.M1, minute: 0));

        order.Should().Equal("big", "small");
    }

    [Fact]
    public void Timeframe_priority_breaks_a_grade_score_and_rr_tie_higher_frame_first()
    {
        // Grade, score, RR all tie — the §4.7 higher-conviction frame (H4 > M5) wins, even when it's OLDER.
        var order = Order(
            Signal("m5", grade: SetupGrade.B, score: 70, rr: 2.5m, timeframe: Timeframe.M5, minute: 100),
            Signal("h4", grade: SetupGrade.B, score: 70, rr: 2.5m, timeframe: Timeframe.H4, minute: 0));

        order.Should().Equal("h4", "m5");
    }

    [Fact]
    public void Recency_breaks_the_final_tie_newer_first()
    {
        // Everything else ties (same grade/score/RR/TF) — the newer detection ranks first.
        var order = Order(
            Signal("older", minute: 0),
            Signal("newer", minute: 30));

        order.Should().Equal("newer", "older");
    }

    [Fact]
    public void Timeframe_priority_is_configurable_and_overrides_the_default_ladder()
    {
        // Re-weight so M5 outranks H4 (the inverse of the default §4.7 ladder); merged BY KEY onto the default.
        var options = new SignalRankingOptions
        {
            TimeframePriority = new Dictionary<Timeframe, int> { [Timeframe.M5] = 100 },
        };
        var ranker = new SignalRanker(options);

        var order = ranker.Rank(
        [
            Signal("h4", timeframe: Timeframe.H4, minute: 0),
            Signal("m5", timeframe: Timeframe.M5, minute: 0),
        ]).Select(s => s.Payload).ToList();

        order.Should().Equal("m5", "h4");
    }

    [Fact]
    public void Ranking_is_deterministic_regardless_of_input_order()
    {
        var a = Signal("a", grade: SetupGrade.A, score: 80);
        var b = Signal("b", grade: SetupGrade.B, score: 90);
        var c = Signal("c", grade: SetupGrade.B, score: 70);

        Order(a, b, c).Should().Equal("a", "b", "c");
        Order(c, b, a).Should().Equal("a", "b", "c");
        Order(b, a, c).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void An_empty_input_yields_an_empty_ranking()
        => Ranker.Rank(Array.Empty<RankableSignal<string>>()).Should().BeEmpty();
}
