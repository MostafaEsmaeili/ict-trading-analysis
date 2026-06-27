using FluentAssertions;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.PaperTrading.Application.Trading;
using IctTrader.PaperTrading.Contracts;

namespace IctTrader.UnitTests.PaperTrading;

/// <summary>
/// Locks the trades read-side filter routing: <see cref="GetTradesQueryHandler"/> must select the source set by the
/// requested status (case-insensitive + trimmed, unrecognised ⇒ all) and narrow by symbol case-insensitively, so the
/// dashboard's trades table can request "open"/"closed"/all for one symbol or all symbols.
/// </summary>
public sealed class GetTradesQueryHandlerTests
{
    private static readonly DateTimeOffset OpenedAt = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static PaperTrade OpenTrade(string symbol)
        => new(Guid.NewGuid(), Guid.NewGuid(), new Symbol(symbol), TradeStyle.Intraday, Timeframe.M5,
               new TradePlan(
                   Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
                   new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m))),
               new PositionSize(0.31m), pipSize: 0.0001m, valuePerPip: 10m, OpenedAt);

    private static PaperTrade ClosedTrade(string symbol)
    {
        var trade = OpenTrade(symbol);
        trade.Close(new Price(1.0920m), TradeCloseReason.TargetHit, TradeCosts.Zero, OpenedAt.AddMinutes(30));
        return trade;
    }

    [Fact]
    public async Task Default_status_returns_all_trades()
    {
        var repo = new FakeRepo([OpenTrade("EURUSD")], [ClosedTrade("GBPUSD")]);

        var result = await new GetTradesQueryHandler(repo).HandleAsync(new GetTradesQuery());

        result.Should().HaveCount(2);
        repo.LastCalled.Should().Be("All");
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("open")]
    [InlineData("  OPEN  ")]
    public async Task Open_status_routes_to_the_open_set_case_insensitively(string status)
    {
        var repo = new FakeRepo([OpenTrade("EURUSD"), OpenTrade("GBPUSD")], [ClosedTrade("EURUSD")]);

        var result = await new GetTradesQueryHandler(repo).HandleAsync(new GetTradesQuery(Status: status));

        result.Should().HaveCount(2);
        repo.LastCalled.Should().Be("Open");
    }

    [Fact]
    public async Task Closed_status_routes_to_the_closed_set()
    {
        var repo = new FakeRepo([OpenTrade("EURUSD")], [ClosedTrade("EURUSD")]);

        var result = await new GetTradesQueryHandler(repo).HandleAsync(new GetTradesQuery(Status: "Closed"));

        result.Should().ContainSingle();
        repo.LastCalled.Should().Be("Closed");
    }

    [Fact]
    public async Task An_unrecognised_status_returns_all()
    {
        var repo = new FakeRepo([OpenTrade("EURUSD")], [ClosedTrade("EURUSD")]);

        var result = await new GetTradesQueryHandler(repo).HandleAsync(new GetTradesQuery(Status: "nonsense"));

        result.Should().HaveCount(2);
        repo.LastCalled.Should().Be("All");
    }

    [Theory]
    [InlineData("eurusd")]
    [InlineData("EURUSD")]
    public async Task Symbol_filter_narrows_case_insensitively(string symbol)
    {
        var repo = new FakeRepo([OpenTrade("EURUSD"), OpenTrade("GBPUSD")], []);

        var result = await new GetTradesQueryHandler(repo).HandleAsync(new GetTradesQuery(Symbol: symbol));

        result.Should().ContainSingle();
        result[0].Symbol.Should().Be("EURUSD");
    }

    private sealed class FakeRepo(IReadOnlyList<PaperTrade> open, IReadOnlyList<PaperTrade> closed)
        : IPaperTradeRepository
    {
        public string? LastCalled { get; private set; }

        public Task<PaperTrade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<PaperTrade?>(null);

        public Task AddAsync(PaperTrade trade, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<PaperTrade>> GetOpenAsync(CancellationToken cancellationToken = default)
        {
            LastCalled = "Open";
            return Task.FromResult(open);
        }

        public Task<IReadOnlyList<PaperTrade>> GetClosedAsync(CancellationToken cancellationToken = default)
        {
            LastCalled = "Closed";
            return Task.FromResult(closed);
        }

        public Task<IReadOnlyList<PaperTrade>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            LastCalled = "All";
            return Task.FromResult<IReadOnlyList<PaperTrade>>([.. open, .. closed]);
        }
    }
}
