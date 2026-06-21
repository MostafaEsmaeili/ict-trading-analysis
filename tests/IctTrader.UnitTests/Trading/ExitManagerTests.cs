using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the §2.5.9/§3.4 <see cref="ExitManager"/>: the per-candle precedence <b>protective-fill → scale → trail</b>,
/// the once-only T1 scale-out sized <c>PartialFraction × Size</c> at the partial level, the trail ratchet listed
/// after the scale in apply order (so the aggregate timeline stays monotonic), and the DECIDE-only shape (it returns
/// an <see cref="ExitPlan"/> the caller applies). The max-hold / no-overnight time-exit is a deferred cut.
/// </summary>
public class ExitManagerTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Open = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BarClose = new(2024, 7, 1, 7, 5, 0, TimeSpan.Zero);
    private static readonly ExitContext Context = new(BarClose);

    private static readonly ExitManager Manager = new(
        new FillEvaluator(new FillOptions()),
        new StopTrailPolicy(new StopTrailOptions()),
        new ExecutionCostModel(new ExecutionCostOptions()),
        new ExitManagementOptions());

    // entry 1.0832, stop 1.0800 (32-pip 1R), T1 1.0864 (= 1R away), runner 1.0920 (+2.75R); 0.30 lots, 10/pip.
    private static PaperTrade BullishTrade()
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0864m), new Price(1.0920m)));
        return new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(0.30m), pipSize: 0.0001m, valuePerPip: 10m, Open);
    }

    // Short mirror: entry 1.0870, stop 1.0900 (30-pip 1R), T1 1.0840 (= 1R away), runner 1.0790.
    private static PaperTrade BearishTrade()
    {
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0840m), new Price(1.0790m)));
        return new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(0.30m), pipSize: 0.0001m, valuePerPip: 10m, Open);
    }

    private static Candle Bar(decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, BarClose, open, high, low, close, 1_000m);

    private static void Apply(PaperTrade trade, ExitPlan plan)
    {
        foreach (var a in plan.Actions)
        {
            switch (a.Kind)
            {
                case ExitActionKind.ScaleOut:
                    trade.ScaleOut(a.Price, a.LegSize!.Value, a.Costs, a.Reason!.Value, a.AtUtc);
                    break;
                case ExitActionKind.MoveStop:
                    trade.MoveStop(a.Price, a.AtUtc);
                    break;
                case ExitActionKind.Close:
                    trade.Close(a.Price, a.Reason!.Value, a.Costs, a.AtUtc);
                    break;
            }
        }
    }

    [Fact]
    public void A_bar_that_hits_the_stop_closes_the_whole_position_and_does_nothing_else()
    {
        var plan = Manager.Decide(BullishTrade(), Bar(1.0830m, 1.0850m, 1.0795m, 1.0805m), Context);

        plan.Actions.Should().ContainSingle();
        var close = plan.Actions[0];
        close.Kind.Should().Be(ExitActionKind.Close);
        close.Reason.Should().Be(TradeCloseReason.StopHit);
        close.Price.Value.Should().Be(1.0800m);
    }

    [Fact]
    public void A_bar_that_hits_the_runner_closes_at_the_target()
    {
        var plan = Manager.Decide(BullishTrade(), Bar(1.0900m, 1.0925m, 1.0890m, 1.0915m), Context);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(ExitActionKind.Close);
        plan.Actions[0].Reason.Should().Be(TradeCloseReason.TargetHit);
        plan.Actions[0].Price.Value.Should().Be(1.0920m);
    }

    [Fact]
    public void A_surviving_bar_at_t1_scales_out_then_trails_to_breakeven()
    {
        var plan = Manager.Decide(BullishTrade(), Bar(1.0850m, 1.0864m, 1.0840m, 1.0858m), Context);

        plan.Actions.Should().HaveCount(2);

        var scale = plan.Actions[0];
        scale.Kind.Should().Be(ExitActionKind.ScaleOut); // scale is applied BEFORE the stop move
        scale.Price.Value.Should().Be(1.0864m);          // booked at the T1 level, not the bar high
        scale.LegSize!.Value.Lots.Should().Be(0.15m);    // 0.50 × the original 0.30
        scale.Reason.Should().Be(TradeCloseReason.TargetHit);
        scale.Costs.Total.Amount.Should().Be(1.95m);     // ComputeExitLeg(0.15): 1.05 spread + 0.90 commission

        plan.Actions[1].Kind.Should().Be(ExitActionKind.MoveStop);
        plan.Actions[1].Price.Value.Should().Be(1.0832m); // breakeven

        // Both actions are stamped at the caller-passed bar-close time.
        plan.Actions.Should().OnlyContain(a => a.AtUtc == BarClose);
    }

    [Fact]
    public void The_decided_scale_then_trail_plan_applies_to_a_trade_without_breaking_the_timeline()
    {
        var trade = BullishTrade();

        Apply(trade, Manager.Decide(trade, Bar(1.0850m, 1.0864m, 1.0840m, 1.0858m), Context));

        trade.HasScaledOut.Should().BeTrue();
        trade.RemainingSize.Lots.Should().Be(0.15m);
        trade.CurrentStop.Value.Should().Be(1.0832m);
        trade.IsBreakevenArmed.Should().BeTrue();
    }

    [Fact]
    public void A_short_trade_at_t1_scales_out_then_trails_to_breakeven_mirrored()
    {
        var plan = Manager.Decide(BearishTrade(), Bar(1.0855m, 1.0860m, 1.0840m, 1.0850m), Context);

        plan.Actions.Should().HaveCount(2);
        plan.Actions[0].Kind.Should().Be(ExitActionKind.ScaleOut);
        plan.Actions[0].Price.Value.Should().Be(1.0840m); // the T1 level
        plan.Actions[0].LegSize!.Value.Lots.Should().Be(0.15m);
        plan.Actions[1].Kind.Should().Be(ExitActionKind.MoveStop);
        plan.Actions[1].Price.Value.Should().Be(1.0870m); // breakeven (entry), ratcheted DOWN from 1.0900
    }

    [Fact]
    public void A_surviving_bar_short_of_t1_only_trails()
    {
        var plan = Manager.Decide(BullishTrade(), Bar(1.0845m, 1.0850m, 1.0840m, 1.0848m), Context);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(ExitActionKind.MoveStop);
        plan.Actions[0].Price.Value.Should().Be(1.0824m); // residual-risk rung (+18 pips, no T1)
    }

    [Fact]
    public void A_re_touch_of_t1_after_a_partial_does_not_scale_again()
    {
        var trade = BullishTrade();
        trade.ScaleOut(new Price(1.0864m), new PositionSize(0.15m), TradeCosts.Zero, TradeCloseReason.TargetHit, Open.AddMinutes(1));

        var plan = Manager.Decide(trade, Bar(1.0850m, 1.0864m, 1.0840m, 1.0858m), Context);

        plan.Actions.Should().NotContain(a => a.Kind == ExitActionKind.ScaleOut); // ScaleOut is once-only
    }

    [Fact]
    public void A_quiet_bar_decides_nothing()
    {
        var plan = Manager.Decide(BullishTrade(), Bar(1.0835m, 1.0840m, 1.0830m, 1.0838m), Context);

        plan.HasActions.Should().BeFalse();
    }

    [Fact]
    public void The_same_inputs_decide_an_identical_plan()
    {
        var trade = BullishTrade();
        var candle = Bar(1.0850m, 1.0864m, 1.0840m, 1.0858m);

        var first = Manager.Decide(trade, candle, Context);
        var second = Manager.Decide(trade, candle, Context);

        first.Actions.Should().Equal(second.Actions); // value-equal actions → replay reproduces live
    }

    [Fact]
    public void A_three_bar_scale_then_runner_books_the_size_weighted_reward()
    {
        var trade = BullishTrade();

        Apply(trade, Manager.Decide(trade, Bar(1.0850m, 1.0864m, 1.0840m, 1.0858m), new ExitContext(Open.AddMinutes(5))));
        Apply(trade, Manager.Decide(trade, Bar(1.0900m, 1.0925m, 1.0890m, 1.0915m), new ExitContext(Open.AddMinutes(10))));

        trade.Status.Should().Be(TradeStatus.Closed);
        trade.RealizedR!.Value.Should().BeApproximately(1.875m, 0.0001m); // 0.5×(+1R) + 0.5×(+2.75R)
    }

    [Fact]
    public void A_closed_trade_cannot_be_managed()
    {
        var trade = BullishTrade();
        trade.Close(new Price(1.0920m), TradeCloseReason.TargetHit, TradeCosts.Zero, BarClose);

        var act = () => Manager.Decide(trade, Bar(1.0850m, 1.0864m, 1.0840m, 1.0858m), Context);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_candle_for_a_different_symbol_is_rejected()
    {
        var foreignBar = new Candle(new Symbol("GBPUSD"), Timeframe.M5, BarClose, 1.0850m, 1.0864m, 1.0840m, 1.0858m, 1_000m);

        var act = () => Manager.Decide(BullishTrade(), foreignBar, Context);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void The_bar_close_time_must_be_utc()
    {
        var local = new DateTimeOffset(2024, 7, 1, 7, 5, 0, TimeSpan.FromHours(-4));

        var act = () => new ExitContext(local);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_default_or_pre_open_context_is_rejected_before_stamping()
    {
        var preOpen = () => Manager.Decide(BullishTrade(), Bar(1.0850m, 1.0864m, 1.0840m, 1.0858m), default);

        preOpen.Should().Throw<DomainException>(); // default(ExitContext).BarCloseUtc is MinValue < the open
    }

    [Fact]
    public void A_plan_is_immutable_and_rejects_null_actions()
    {
        var source = new List<ExitAction> { ExitAction.MoveStop(new Price(1.0820m), BarClose) };
        var plan = new ExitPlan(source);

        source.Clear(); // mutating the source must not change the plan

        plan.Actions.Should().ContainSingle();
        ((Action)(() => _ = new ExitPlan(null!))).Should().Throw<ArgumentNullException>();
    }
}
