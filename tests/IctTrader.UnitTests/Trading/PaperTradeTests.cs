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

    // entry 1.0832, stop 1.0800 (32-pip 1R), T2 1.0920; 10/pip. Default 0.31 lots; partial tests use 0.30 so a
    // clean half (0.15) and the +1R (1.0864) / +3R (1.0928) levels stay tidy.
    private static PaperTrade BullishTrade(decimal lots = 0.31m)
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(lots), pipSize: 0.0001m, valuePerPip: 10m, Open);
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
    public void Scaling_out_then_running_blends_realized_r_size_weighted()
    {
        var trade = BullishTrade(0.30m); // RiskBudget 96 (32 pips * 3.0/pip)

        trade.ScaleOut(new Price(1.0864m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later);
        trade.RemainingSize.Lots.Should().Be(0.15m);
        trade.HasScaledOut.Should().BeTrue();
        trade.Lifecycle.Should().Be(TradeLifecycle.PartialTaken);

        trade.Close(new Price(1.0928m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later); // +3R on the runner half

        trade.RealizedR!.Value.Should().Be(2.0m);       // 0.5*(+1R) + 0.5*(+3R)
        trade.GrossPnl!.Value.Amount.Should().Be(192m); // 48 + 144
        trade.Lifecycle.Should().Be(TradeLifecycle.Closed);
        trade.Status.Should().Be(TradeStatus.Closed);
    }

    [Fact]
    public void A_short_trade_scale_out_blends_the_realized_reward_mirrored()
    {
        // Short: entry 1.0870, stop 1.0900 (30-pip 1R), 0.30 lots, 10/pip -> RiskBudget 90.
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0830m), new Price(1.0790m)));
        var trade = new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(0.30m), pipSize: 0.0001m, valuePerPip: 10m, Open);

        trade.ScaleOut(new Price(1.0840m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later); // +1R
        trade.Close(new Price(1.0780m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later);                            // +3R

        trade.RealizedR!.Value.Should().Be(2.0m);       // 0.5*(+1R) + 0.5*(+3R)
        trade.GrossPnl!.Value.Amount.Should().Be(180m); // 45 + 135
    }

    [Fact]
    public void The_gross_to_r_identity_holds_for_a_scaled_trade()
    {
        var trade = BullishTrade(0.30m);
        trade.ScaleOut(new Price(1.0864m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later);
        trade.Close(new Price(1.0928m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later);

        // RealizedR is DERIVED as gross / risk, so multiplying back recovers gross to within decimal rounding
        // (an exact-equality assertion would crack on non-terminating ratios — see the regression below).
        (trade.RealizedR!.Value * trade.RiskBudget.Amount).Should().BeApproximately(trade.GrossPnl!.Value.Amount, 1e-7m);
    }

    [Fact]
    public void The_gross_to_r_identity_survives_a_non_terminating_blended_ratio()
    {
        // 0.07 flat partial + 0.24 runner of a 0.31-lot 32-pip trade → gross 2.40, RiskBudget 99.2, R = 2.40/99.2
        // is non-terminating. The derive-one-from-the-other design keeps R and money consistent to rounding; an
        // exact `gross == R * risk` assertion would fail here (2.40 vs 2.4000…0003).
        var trade = BullishTrade(0.31m);
        trade.ScaleOut(new Price(1.0832m), new PositionSize(0.07m), TradeCosts.Zero, TradeCloseReason.Manual, Later);
        trade.Close(new Price(1.0833m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later);

        trade.GrossPnl!.Value.Amount.Should().Be(2.4m);
        (trade.RealizedR!.Value * trade.RiskBudget.Amount).Should().BeApproximately(trade.GrossPnl!.Value.Amount, 1e-7m);
    }

    [Fact]
    public void A_partial_then_a_breakeven_runner_books_only_the_partial_r()
    {
        var trade = BullishTrade(0.30m);
        trade.ScaleOut(new Price(1.0864m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later);

        trade.Close(new Price(1.0832m), TradeCloseReason.Manual, TradeCosts.Zero, Later); // runner flat at entry = 0R

        trade.RealizedR!.Value.Should().Be(0.5m);      // 0.5*(+1R) + 0.5*(0R)
        trade.GrossPnl!.Value.Amount.Should().Be(48m);
    }

    [Fact]
    public void Net_subtracts_every_legs_costs_while_gross_r_is_unchanged()
    {
        var trade = BullishTrade(0.30m);
        var legCost = new TradeCosts(new Money(1.0m), new Money(0.9m)); // 1.90 each leg

        trade.ScaleOut(new Price(1.0864m), new PositionSize(0.15m), legCost, TradeCloseReason.TargetHit, Later);
        trade.Close(new Price(1.0928m), TradeCloseReason.TargetHit, legCost, Later);

        trade.Costs!.Value.Amount.Should().Be(3.8m);         // 1.90 partial + 1.90 runner
        trade.RealizedPnl!.Value.Amount.Should().Be(188.2m); // gross 192 − 3.80
        trade.RealizedR!.Value.Should().Be(2.0m);            // structural R is not reduced by costs
    }

    [Fact]
    public void Scaling_out_raises_a_partial_closed_event_with_derived_leg_figures()
    {
        var trade = BullishTrade(0.30m);

        trade.ScaleOut(new Price(1.0864m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later);

        var ev = trade.DomainEvents.OfType<PaperTradePartialClosed>().Should().ContainSingle().Subject;
        ev.LegR.Should().Be(1.0m);
        ev.LegGross.Amount.Should().Be(48m);
        ev.Fraction.Should().Be(0.5m);
        ev.RemainingSize.Lots.Should().Be(0.15m);
    }

    [Fact]
    public void A_full_close_with_no_partial_books_one_leg_and_todays_figures()
    {
        var trade = BullishTrade(0.31m);

        trade.Close(new Price(1.0920m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later);

        trade.Legs.Should().ContainSingle();
        trade.HasScaledOut.Should().BeFalse();
        trade.RealizedR!.Value.Should().BeApproximately(2.75m, 0.0001m);
        trade.GrossPnl!.Value.Amount.Should().Be(272.8m);
    }

    [Fact]
    public void A_paper_trade_takes_only_one_partial()
    {
        var trade = BullishTrade(0.30m);
        trade.ScaleOut(new Price(1.0864m), new PositionSize(0.10m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later);

        var act = () =>
            trade.ScaleOut(new Price(1.0870m), new PositionSize(0.10m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_partial_must_close_strictly_fewer_lots_than_remain()
    {
        var trade = BullishTrade(0.30m);

        var full = () =>
            trade.ScaleOut(new Price(1.0864m), new PositionSize(0.30m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later);

        full.Should().Throw<DomainException>(); // an equal-size exit is a Close, not a scale
    }

    [Fact]
    public void Scale_out_timestamps_must_be_utc_and_not_before_open()
    {
        var local = new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.FromHours(-4));

        var nonUtc = () => BullishTrade(0.30m)
            .ScaleOut(new Price(1.0864m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, local);
        var beforeOpen = () => BullishTrade(0.30m).ScaleOut(
            new Price(1.0864m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, Open.AddMinutes(-1));

        nonUtc.Should().Throw<DomainException>();
        beforeOpen.Should().Throw<DomainException>();
    }

    [Fact]
    public void Cannot_scale_out_a_closed_trade()
    {
        var trade = BullishTrade(0.30m);
        trade.Close(new Price(1.0920m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later);

        var act = () =>
            trade.ScaleOut(new Price(1.0864m), new PositionSize(0.10m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later);

        act.Should().Throw<DomainException>();
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
