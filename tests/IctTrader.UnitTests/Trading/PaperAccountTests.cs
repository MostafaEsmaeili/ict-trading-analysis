using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the <see cref="PaperAccount"/> invariants (plan §3.0/§5.1/§2.5.10): equity opens positive, the
/// aggregate open-risk cap is enforced before a trade reserves risk, settlement releases the reserved risk and
/// books the realized P&amp;L, and the trade-id ledger keeps register/settle account-scoped and idempotent.
/// </summary>
public class PaperAccountTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Utc = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static PaperAccount Account(decimal equity = 10_000m, decimal cap = 5m) =>
        new(Guid.NewGuid(), new Money(equity), cap);

    // A directly-built open trade whose derived risk budget is lots * 32 pips * 10/pip = lots * 320.
    private static PaperTrade OpenTrade(Guid accountId, decimal lots)
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new PaperTrade(
            Guid.NewGuid(), accountId, Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(lots), pipSize: 0.0001m, valuePerPip: 10m, Utc);
    }

    [Fact]
    public void Opens_with_positive_equity_and_no_open_risk()
    {
        var account = Account();

        account.Equity.Amount.Should().Be(10_000m);
        account.OpenRisk.Amount.Should().Be(0m);
        account.OpenRiskCap.Amount.Should().Be(500m); // 5% of 10,000
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_non_positive_starting_equity(decimal equity)
    {
        var act = () => new PaperAccount(Guid.NewGuid(), new Money(equity), 5m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Rejects_an_empty_id_or_an_out_of_range_cap()
    {
        var emptyId = () => new PaperAccount(Guid.Empty, new Money(10_000m), 5m);
        var badCap = () => new PaperAccount(Guid.NewGuid(), new Money(10_000m), 0m);

        emptyId.Should().Throw<DomainException>();
        badCap.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reserves_an_open_trade_within_the_portfolio_cap()
    {
        var account = Account();

        account.RegisterOpen(OpenTrade(account.Id, 0.31m)); // 99.2 risk

        account.OpenRisk.Amount.Should().Be(99.2m);
    }

    [Fact]
    public void Refuses_to_reserve_risk_beyond_the_cap()
    {
        var account = Account();
        account.RegisterOpen(OpenTrade(account.Id, 1.5m)); // 480 of the 500 cap used

        var act = () => account.RegisterOpen(OpenTrade(account.Id, 0.1m)); // +32 -> 512 > 500

        act.Should().Throw<DomainException>();
        account.OpenRisk.Amount.Should().Be(480m); // unchanged — the reservation was atomic
    }

    [Fact]
    public void Refuses_a_trade_that_belongs_to_a_different_account()
    {
        var account = Account();

        var act = () => account.RegisterOpen(OpenTrade(Guid.NewGuid(), 0.31m));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Refuses_to_reserve_the_same_trade_twice()
    {
        var account = Account();
        var trade = OpenTrade(account.Id, 0.31m);
        account.RegisterOpen(trade);

        var act = () => account.RegisterOpen(trade);

        act.Should().Throw<DomainException>();
        account.OpenRisk.Amount.Should().Be(99.2m);
    }

    [Fact]
    public void Settling_a_closed_trade_releases_its_risk_and_books_the_pnl()
    {
        var account = Account();
        var trade = OpenTrade(account.Id, 0.31m);
        account.RegisterOpen(trade);
        trade.Close(new Price(1.0920m), TradeCloseReason.TargetHit, Utc.AddHours(1)); // +272.8

        account.Settle(trade);

        account.OpenRisk.Amount.Should().Be(0m);
        account.Equity.Amount.Should().Be(10_272.8m);
    }

    [Fact]
    public void Cannot_settle_an_open_or_unreserved_trade_and_cannot_settle_twice()
    {
        var account = Account();
        var trade = OpenTrade(account.Id, 0.31m);
        account.RegisterOpen(trade);

        var settleWhileOpen = () => account.Settle(trade);
        settleWhileOpen.Should().Throw<DomainException>(); // still Open

        trade.Close(new Price(1.0920m), TradeCloseReason.TargetHit, Utc.AddHours(1));
        account.Settle(trade);

        var settleAgain = () => account.Settle(trade);
        settleAgain.Should().Throw<DomainException>(); // already settled — id no longer on the ledger
    }

    [Fact]
    public void A_settlement_cannot_drive_equity_to_zero_or_below()
    {
        var account = Account(equity: 100m); // cap 5 -> a 0.01-lot trade risks 3.2, well within
        var trade = OpenTrade(account.Id, 0.01m);
        account.RegisterOpen(trade);
        trade.Close(new Price(0.9000m), TradeCloseReason.Manual, Utc.AddHours(1)); // catastrophic loss

        var act = () => account.Settle(trade);

        act.Should().Throw<DomainException>();
    }
}
