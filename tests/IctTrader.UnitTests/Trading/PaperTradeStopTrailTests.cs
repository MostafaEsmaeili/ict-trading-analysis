using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the §2.5.9 stop-trail MECHANISM on <see cref="PaperTrade"/>: <see cref="PaperTrade.MoveStop"/> ratchets the
/// live <see cref="PaperTrade.CurrentStop"/> toward profit ONLY (never loosens, may cross entry to lock profit, may
/// not reach the runner), the frozen <see cref="PaperTrade.RiskBudget"/> and R denominator are untouched,
/// <see cref="PaperTrade.IsBreakevenArmed"/> is a derived flag orthogonal to the partial state, and a move raises
/// <see cref="PaperTradeStopMoved"/>. The candle-driven trail-ladder policy is a separate slice.
/// </summary>
public class PaperTradeStopTrailTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Open = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2024, 7, 1, 8, 30, 0, TimeSpan.Zero);

    // entry 1.0832, stop 1.0800 (32-pip 1R), T1 1.0876, runner 1.0920.
    private static PaperTrade BullishTrade(decimal lots = 0.30m)
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(lots), pipSize: 0.0001m, valuePerPip: 10m, Open);
    }

    // entry 1.0870, stop 1.0900 (30-pip 1R), runner 1.0790.
    private static PaperTrade BearishTrade(decimal lots = 0.30m)
    {
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0820m), new Price(1.0790m)));
        return new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(lots), pipSize: 0.0001m, valuePerPip: 10m, Open);
    }

    [Fact]
    public void Current_stop_starts_at_the_plan_stop()
    {
        var trade = BullishTrade();

        trade.CurrentStop.Value.Should().Be(1.0800m);
        trade.Stop.Value.Should().Be(1.0800m);
        trade.IsBreakevenArmed.Should().BeFalse();
    }

    [Fact]
    public void Moving_the_stop_up_ratchets_the_live_stop_without_touching_the_frozen_risk()
    {
        var trade = BullishTrade();
        var riskBefore = trade.RiskBudget.Amount;

        trade.MoveStop(new Price(1.0816m), Later); // tighter, still below entry

        trade.CurrentStop.Value.Should().Be(1.0816m);
        trade.Stop.Value.Should().Be(1.0800m);           // the frozen original is unchanged
        trade.RiskBudget.Amount.Should().Be(riskBefore); // reserved risk is unchanged
        trade.IsBreakevenArmed.Should().BeFalse();
    }

    [Fact]
    public void Moving_the_stop_to_entry_arms_breakeven()
        => BullishTradeWithStopAt(1.0832m).IsBreakevenArmed.Should().BeTrue();

    [Fact]
    public void The_stop_can_ratchet_past_entry_to_lock_profit()
    {
        var trade = BullishTrade();
        trade.MoveStop(new Price(1.0832m), Later); // breakeven
        trade.MoveStop(new Price(1.0850m), Later); // profit-lock

        trade.CurrentStop.Value.Should().Be(1.0850m);
        trade.IsBreakevenArmed.Should().BeTrue();
    }

    [Fact]
    public void A_long_stop_cannot_loosen()
    {
        var trade = BullishTrade();
        trade.MoveStop(new Price(1.0820m), Later);

        var act = () => trade.MoveStop(new Price(1.0810m), Later); // lower = loosen

        act.Should().Throw<DomainException>();
        trade.CurrentStop.Value.Should().Be(1.0820m);
    }

    [Fact]
    public void A_short_stop_only_ratchets_down()
    {
        var trade = BearishTrade();
        trade.MoveStop(new Price(1.0880m), Later); // down from 1.0900 = tighten

        trade.CurrentStop.Value.Should().Be(1.0880m);

        var loosen = () => trade.MoveStop(new Price(1.0890m), Later); // up = loosen
        loosen.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_stop_cannot_trail_to_or_past_the_runner_target()
    {
        var trade = BullishTrade();

        var act = () => trade.MoveStop(new Price(1.0920m), Later); // = runner

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_closed_trade_cannot_move_its_stop()
    {
        var trade = BullishTrade();
        trade.Close(new Price(1.0920m), TradeCloseReason.TargetHit, TradeCosts.Zero, Later);

        var act = () => trade.MoveStop(new Price(1.0840m), Later);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_stop_move_must_be_utc_and_not_before_open()
    {
        var local = new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.FromHours(-4));

        var nonUtc = () => BullishTrade().MoveStop(new Price(1.0820m), local);
        var beforeOpen = () => BullishTrade().MoveStop(new Price(1.0820m), Open.AddMinutes(-1));

        nonUtc.Should().Throw<DomainException>();
        beforeOpen.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_stop_move_cannot_predate_the_last_scale_out()
    {
        var trade = BullishTrade();
        trade.ScaleOut(new Price(1.0864m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later);

        var act = () => trade.MoveStop(new Price(1.0820m), Open.AddMinutes(30)); // 07:30 < the 08:30 partial

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Moving_the_stop_raises_a_stop_moved_event_with_a_breakeven_snapshot()
    {
        var trade = BullishTrade();

        trade.MoveStop(new Price(1.0832m), Later); // entry = breakeven

        var ev = trade.DomainEvents.OfType<PaperTradeStopMoved>().Should().ContainSingle().Subject;
        ev.PreviousStop.Value.Should().Be(1.0800m);
        ev.NewStop.Value.Should().Be(1.0832m);
        ev.BreakevenArmed.Should().BeTrue();
    }

    [Fact]
    public void Breakeven_armed_composes_with_the_partial_state()
    {
        var trade = BullishTrade();
        trade.ScaleOut(new Price(1.0864m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, Later);
        trade.MoveStop(new Price(1.0832m), Later);

        trade.Lifecycle.Should().Be(TradeLifecycle.PartialTaken); // the partial state is preserved
        trade.IsBreakevenArmed.Should().BeTrue();                 // and breakeven-armed (orthogonal)
    }

    [Fact]
    public void A_scale_out_cannot_predate_an_earlier_stop_move()
    {
        var trade = BullishTrade();
        trade.MoveStop(new Price(1.0820m), Later); // stop move at 08:30

        // A partial stamped before the stop move would make the management timeline non-monotonic.
        var act = () =>
            trade.ScaleOut(new Price(1.0864m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, Open.AddMinutes(30));

        act.Should().Throw<DomainException>();
    }

    private static PaperTrade BullishTradeWithStopAt(decimal stop)
    {
        var trade = BullishTrade();
        trade.MoveStop(new Price(stop), Later);
        return trade;
    }
}
