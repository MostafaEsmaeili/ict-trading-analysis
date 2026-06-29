using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Scanning.Application.Signals;
using IctTrader.Scanning.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks the signals read-side (plan §9): <see cref="GetSignalsQueryHandler"/> returns the ranked top-N from the live
/// feed, filtered by symbol / style / grade floor and capped by max. The full ranking order itself is proven in
/// <c>SignalRankerTests</c>; here we prove the filter + cap routing through the real service/store/ranker.
/// </summary>
public sealed class GetSignalsQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static SetupDto Setup(
        string symbol, string style = "Intraday", string grade = "B", int score = 70, int minutesAgo = 5)
        => new(
            Id: Guid.NewGuid(),
            Symbol: symbol,
            Direction: "Bullish",
            Killzone: "LondonOpen",
            Style: style,
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

    private static (GetSignalsQueryHandler Handler, SignalFeedStore Store) Build(SignalRankingOptions? options = null)
    {
        var opts = options ?? new SignalRankingOptions();
        var store = new SignalFeedStore(opts);
        var service = new SignalRankingService(store, new SignalRanker(opts), opts);
        var handler = new GetSignalsQueryHandler(service, new FakeTimeProvider(Now));
        return (handler, store);
    }

    [Fact]
    public async Task It_returns_the_ranked_feed_with_one_based_rank_and_the_score()
    {
        var (handler, store) = Build();
        store.Add(Setup("EURUSD", grade: "A", score: 85), Now);
        store.Add(Setup("GBPUSD", grade: "B", score: 90), Now);

        var result = await handler.HandleAsync(new GetSignalsQuery());

        result.Should().HaveCount(2);
        result[0].Rank.Should().Be(1);
        result[0].Setup.Symbol.Should().Be("EURUSD"); // grade A beats grade B
        result[0].Score.Should().Be(85);
        result[1].Rank.Should().Be(2);
        result[1].Setup.Symbol.Should().Be("GBPUSD");
    }

    [Theory]
    [InlineData("EURUSD")]
    [InlineData("eurusd")]
    public async Task The_symbol_filter_narrows_case_insensitively(string symbol)
    {
        var (handler, store) = Build();
        store.Add(Setup("EURUSD"), Now);
        store.Add(Setup("GBPUSD"), Now);

        var result = await handler.HandleAsync(new GetSignalsQuery(Symbol: symbol));

        result.Should().ContainSingle().Which.Setup.Symbol.Should().Be("EURUSD");
    }

    [Fact]
    public async Task The_style_filter_narrows_case_insensitively()
    {
        var (handler, store) = Build();
        store.Add(Setup("EURUSD", style: "Intraday"), Now);
        store.Add(Setup("EURUSD", style: "Swing"), Now);

        var result = await handler.HandleAsync(new GetSignalsQuery(Style: "swing"));

        result.Should().ContainSingle().Which.Setup.Style.Should().Be("Swing");
    }

    [Fact]
    public async Task The_grade_filter_is_a_floor_A_returns_only_A()
    {
        var (handler, store) = Build();
        store.Add(Setup("EURUSD", grade: "A"), Now);
        store.Add(Setup("GBPUSD", grade: "B"), Now);

        var result = await handler.HandleAsync(new GetSignalsQuery(MinGrade: "A"));

        result.Should().ContainSingle().Which.Setup.Grade.Should().Be("A");
    }

    [Fact]
    public async Task A_requested_grade_floor_can_only_tighten_never_loosen_the_configured_floor()
    {
        // The configured floor is B (the §2.5.4 alert floor). A request for "C" must NOT admit a C setup.
        var (handler, store) = Build(new SignalRankingOptions { MinGrade = IctTrader.Domain.Setups.SetupGrade.B });
        store.Add(Setup("EURUSD", grade: "B"), Now);
        store.Add(Setup("GBPUSD", grade: "C"), Now);

        var result = await handler.HandleAsync(new GetSignalsQuery(MinGrade: "C"));

        result.Should().ContainSingle().Which.Setup.Grade.Should().Be("B");
    }

    [Fact]
    public async Task Max_caps_the_returned_top_n()
    {
        var (handler, store) = Build();
        store.Add(Setup("EURUSD", score: 90), Now);
        store.Add(Setup("GBPUSD", score: 80), Now);
        store.Add(Setup("USDJPY", score: 70), Now);

        var result = await handler.HandleAsync(new GetSignalsQuery(Max: 2));

        result.Should().HaveCount(2);
        result.Select(r => r.Setup.Symbol).Should().Equal("EURUSD", "GBPUSD"); // highest scores
    }

    [Fact]
    public async Task An_empty_feed_returns_an_empty_list()
    {
        var (handler, _) = Build();

        var result = await handler.HandleAsync(new GetSignalsQuery());

        result.Should().BeEmpty();
    }
}
