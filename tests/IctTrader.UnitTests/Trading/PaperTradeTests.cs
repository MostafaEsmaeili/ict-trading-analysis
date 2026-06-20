using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the <see cref="PaperTrade"/> lifecycle (plan §5.1/§5.2/§5.4): construction opens the trade and freezes
/// the original 1R, a close realizes R against that frozen risk (−1R at the stop, the plan RR at the runner) and
/// the gross/net P&amp;L (§5.4 costs subtracted to the net while the price-based R is unchanged), the bearish case
/// mirrors, and a close is legal only once and only from an open trade.
/// </summary>
public class PaperTradeTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Open = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2024, 7, 1, 8, 30, 0, TimeSpan.Zero);

    // entry 1.0832, stop 1.0800 (32-pip 1R), T2 1.0920; sized 0.31 lots, 10/pip -> 3.1/pip for the position.
    private static PaperTrade BullishTrade()
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(0.31m), pipSize: 0.0001m, valuePerPip: 10m, Open);
    }

    [Fact]
    public void Construction_opens_the_trade_freezes_the_risk_and_raises_an_opened_event()
    {
        var trade = BullishTrade();

        trade.Status.Should().Be(TradeStatus.Open);
        trade.Direction.Should().Be(Direction.Bullish);
        trade.InitialRiskPerUnit.Should().Be(0.0032m);
        trade.RiskBudget.Amount.Should().Be(99.2m); // derived from the trade's own geometry (32 pips * 10 * 0.31)
        trade.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<PaperTradeOpened>();
    }

    [Fact]
    public void Closing_at_the_runner_target_realizes_the_plan_reward_to_risk()
    {
        var trade = BullishTrade();

        trade.Close(new Price(1.0920m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later);

        trade.Status.Should().Be(TradeStatus.Closed);
        trade.RealizedR!.Value.Should().BeApproximately(2.75m, 0.0001m); // 88 pips / 32 pips
        trade.RealizedPnl!.Value.Amount.Should().Be(272.8m);            // 88 pips * 3.1 per pip
        trade.DomainEvents.OfType<PaperTradeClosed>().Should().ContainSingle();
    }

    [Fact]
    public void Closing_at_the_stop_realizes_exactly_minus_one_r_and_the_risk_budget()
    {
        var trade = BullishTrade();

        trade.Close(new Price(1.0800m), TradeCloseReason.StopHit, TradeCosts.Zero, Later);

        trade.RealizedR!.Value.Should().Be(-1m);
        trade.RealizedPnl!.Value.Amount.Should().Be(-99.2m);
    }

    [Fact]
    public void A_short_trade_mirrors_the_realized_reward()
    {
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0830m), new Price(1.0790m)));
        var trade = new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(0.30m), pipSize: 0.0001m, valuePerPip: 10m, Open);

        trade.Close(new Price(1.0790m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later);

        trade.RealizedR!.Value.Should().BeApproximately(2.6667m, 0.001m); // 80 pips / 30 pips
        trade.RealizedPnl!.Value.IsPositive.Should().BeTrue();
    }

    [Fact]
    public void Closing_with_costs_books_net_pnl_while_the_price_based_r_stays_gross()
    {
        var trade = BullishTrade();
        var costs = new TradeCosts(new Money(4.34m), new Money(1.86m)); // round-trip spread + commission = 6.20

        trade.Close(new Price(1.0800m), TradeCloseReason.StopHit, costs, Later);

        trade.GrossPnl!.Value.Amount.Should().Be(-99.2m);
        trade.Costs!.Value.Amount.Should().Be(6.2m);
        trade.RealizedPnl!.Value.Amount.Should().Be(-105.4m); // gross −99.2 − costs 6.20
        trade.NetPnl!.Value.Amount.Should().Be(-105.4m);
        trade.RealizedR!.Value.Should().Be(-1m);              // structural R is unchanged by costs
        trade.NetR!.Value.Should().BeApproximately(-1.0625m, 0.0001m); // −105.4 / 99.2
    }

    [Fact]
    public void Closing_with_zero_costs_leaves_net_equal_to_gross()
    {
        var trade = BullishTrade();

        trade.Close(new Price(1.0920m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later);

        trade.GrossPnl!.Value.Amount.Should().Be(272.8m);
        trade.Costs!.Value.Amount.Should().Be(0m);
        trade.NetPnl!.Value.Amount.Should().Be(272.8m);
        trade.NetR!.Value.Should().Be(trade.RealizedR!.Value);
    }

    [Fact]
    public void A_trade_can_only_be_closed_once_and_only_while_open()
    {
        var trade = BullishTrade();
        trade.Close(new Price(1.0920m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later);

        var act = () => trade.Close(new Price(1.0876m), TradeCloseReason.Manual, TradeCosts.Zero, Later);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Open_and_close_timestamps_must_be_utc()
    {
        var local = new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.FromHours(-4));
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));

        var nonUtcOpen = () => new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(0.31m), 0.0001m, 10m, local);
        var nonUtcClose = () => BullishTrade().Close(new Price(1.0920m), TradeCloseReason.TargetHit, TradeCosts.Zero, local);

        nonUtcOpen.Should().Throw<DomainException>();
        nonUtcClose.Should().Throw<DomainException>();
    }
}
