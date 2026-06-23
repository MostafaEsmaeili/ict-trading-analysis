using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the §2.5.9/§3.4 <see cref="ExitManager"/>: the per-candle precedence
/// <b>protective-fill → time-exit → scale → trail</b>, the once-only T1 scale-out sized <c>PartialFraction × Size</c>
/// at the partial level, the trail ratchet listed after the scale in apply order (so the aggregate timeline stays
/// monotonic), the max-hold / no-overnight time-exit (§2.5.1 step 9) that flattens at the bar close but never
/// outranks a real fill, and the DECIDE-only shape (it returns an <see cref="ExitPlan"/> the caller applies).
/// </summary>
public class ExitManagerTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Open = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BarClose = new(2024, 7, 1, 7, 5, 0, TimeSpan.Zero);
    private static readonly ExitContext Context = new(BarClose);

    // NyClock's date conversion is pure (it never reads UtcNow on the time-exit path), but feed it a FakeTimeProvider
    // anyway so no test touches an ambient clock.
    private static readonly NyClock NyClock = new(new FakeTimeProvider());

    private static readonly ExitManager Manager = new(
        new FillEvaluator(new FillOptions()),
        new StopTrailPolicy(new StopTrailOptions()),
        new ExecutionCostModel(new ExecutionCostOptions()),
        new ExitManagementOptions(),
        NyClock,
        new TradeStyleOptions());

    // entry 1.0832, stop 1.0800 (32-pip 1R), T1 1.0864 (= 1R away), runner 1.0920 (+2.75R); 0.30 lots, 10/pip.
    private static PaperTrade BullishTrade() => BullishTradeOpenedAt(Open);

    private static PaperTrade BullishTradeOpenedAt(DateTimeOffset openedAt, TradeStyle style = TradeStyle.Intraday)
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0864m), new Price(1.0920m)));
        return new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, style, Timeframe.M5,
            plan, new PositionSize(0.30m), pipSize: 0.0001m, valuePerPip: 10m, openedAt);
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

    private static Candle BarAt(DateTimeOffset openTime, decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, openTime, open, high, low, close, 1_000m);

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

    [Fact]
    public void An_exit_action_rejects_a_non_utc_timestamp()
    {
        var local = new DateTimeOffset(2024, 7, 1, 7, 5, 0, TimeSpan.FromHours(-4));

        ((Action)(() => _ = ExitAction.MoveStop(new Price(1.0820m), local))).Should().Throw<DomainException>();
    }

    // ---- Time-exit (§2.5.1 step 9: max hold 90–120 min; no overnight) ------------------------------------------

    [Fact]
    public void Reaching_the_style_max_hold_force_closes_the_whole_position_at_the_bar_close()
    {
        // Intraday max-hold is 120 min (Mentorship-verbatim, Ep21). At exactly 120 min on a quiet surviving bar the
        // trade flattens at the bar CLOSE as a TimeExit (a time event, not a price level).
        var trade = BullishTrade();
        var barClose = Open.AddMinutes(120);
        var quiet = BarAt(barClose, 1.0835m, 1.0840m, 1.0830m, 1.0838m);

        var plan = Manager.Decide(trade, quiet, new ExitContext(barClose));

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(ExitActionKind.Close);
        plan.Actions[0].Reason.Should().Be(TradeCloseReason.TimeExit);
        plan.Actions[0].Price.Value.Should().Be(1.0838m);          // the bar close, not a stop/target level
        plan.Actions[0].AtUtc.Should().Be(barClose);
        // A Close carries no LegSize — it flattens the whole RemainingSize (asserted in the apply-to-trade test).
    }

    [Fact]
    public void A_bar_one_minute_short_of_the_max_hold_does_not_time_exit()
    {
        var trade = BullishTrade();
        var barClose = Open.AddMinutes(119);
        var quiet = BarAt(barClose, 1.0835m, 1.0840m, 1.0830m, 1.0838m);

        var plan = Manager.Decide(trade, quiet, new ExitContext(barClose));

        plan.HasActions.Should().BeFalse(); // 119 < 120, same NY day → nothing fires
    }

    [Fact]
    public void Crossing_the_ny_midnight_boundary_force_closes_a_no_overnight_trade()
    {
        // Entry 23:30 NY (03:30Z next day), bar 00:15 NY next day (04:15Z) — only ~45 min elapsed, so max-hold has
        // NOT fired; the close is driven purely by crossing the 00:00 NY financial-day boundary (§2.1).
        var entry = new DateTimeOffset(2024, 7, 2, 3, 30, 0, TimeSpan.Zero);   // 23:30 NY, Jul 1
        var barClose = new DateTimeOffset(2024, 7, 2, 4, 15, 0, TimeSpan.Zero); // 00:15 NY, Jul 2
        var trade = BullishTradeOpenedAt(entry);
        var quiet = BarAt(barClose, 1.0835m, 1.0840m, 1.0830m, 1.0838m);

        var plan = Manager.Decide(trade, quiet, new ExitContext(barClose));

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(ExitActionKind.Close);
        plan.Actions[0].Reason.Should().Be(TradeCloseReason.TimeExit);
        plan.Actions[0].Price.Value.Should().Be(1.0838m);
    }

    [Fact]
    public void A_bar_in_the_same_ny_day_does_not_trigger_a_no_overnight_exit()
    {
        var entry = new DateTimeOffset(2024, 7, 2, 3, 30, 0, TimeSpan.Zero);   // 23:30 NY, Jul 1
        var barClose = new DateTimeOffset(2024, 7, 2, 3, 45, 0, TimeSpan.Zero); // 23:45 NY, Jul 1 (same NY date)
        var trade = BullishTradeOpenedAt(entry);
        var quiet = BarAt(barClose, 1.0835m, 1.0840m, 1.0830m, 1.0838m);

        var plan = Manager.Decide(trade, quiet, new ExitContext(barClose));

        plan.HasActions.Should().BeFalse(); // same NY day, 15 min in → no time-exit
    }

    [Fact]
    public void A_style_that_allows_overnight_is_not_force_closed_when_the_ny_day_rolls()
    {
        // A Swing trade allows overnight (AllowOvernight = true) and has a multi-day max-hold, so crossing the NY
        // midnight boundary does NOT force it out — only the no-overnight styles (Scalp/Intraday) do.
        var entry = new DateTimeOffset(2024, 7, 2, 3, 30, 0, TimeSpan.Zero);
        var barClose = new DateTimeOffset(2024, 7, 2, 4, 15, 0, TimeSpan.Zero); // crosses 00:00 NY
        var trade = BullishTradeOpenedAt(entry, TradeStyle.Swing);
        var quiet = BarAt(barClose, 1.0835m, 1.0840m, 1.0830m, 1.0838m);

        var plan = Manager.Decide(trade, quiet, new ExitContext(barClose));

        plan.HasActions.Should().BeFalse();
    }

    [Fact]
    public void A_real_stop_fill_on_the_max_hold_bar_books_the_stop_not_a_time_exit()
    {
        // SAFETY-CRITICAL: a bar that is BOTH past the max hold AND hit the stop must book the stop fill at the LEVEL
        // (−1R), never a flattering bar-close TimeExit. Booking the time-exit here would understate realized risk.
        var trade = BullishTrade();
        var barClose = Open.AddMinutes(120);
        var stopBar = BarAt(barClose, 1.0820m, 1.0825m, 1.0795m, 1.0805m); // Low 1.0795 ≤ stop 1.0800

        var plan = Manager.Decide(trade, stopBar, new ExitContext(barClose));

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(ExitActionKind.Close);
        plan.Actions[0].Reason.Should().Be(TradeCloseReason.StopHit); // the fill wins, not TimeExit
        plan.Actions[0].Price.Value.Should().Be(1.0800m);             // the stop level, not the 1.0805 bar close
    }

    [Fact]
    public void A_real_runner_fill_on_the_max_hold_bar_books_the_target_not_a_time_exit()
    {
        var trade = BullishTrade();
        var barClose = Open.AddMinutes(120);
        var runnerBar = BarAt(barClose, 1.0900m, 1.0925m, 1.0890m, 1.0915m); // High 1.0925 ≥ runner 1.0920

        var plan = Manager.Decide(trade, runnerBar, new ExitContext(barClose));

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Reason.Should().Be(TradeCloseReason.TargetHit);
        plan.Actions[0].Price.Value.Should().Be(1.0920m);
    }

    [Fact]
    public void A_time_exit_overrides_a_same_bar_scale_out()
    {
        // The bar reaches T1 (would normally scale a partial) but is also past the max hold — the time-exit flattens
        // the whole position instead; no partial is taken on the bar you are force-closing.
        var trade = BullishTrade();
        var barClose = Open.AddMinutes(120);
        var t1Bar = BarAt(barClose, 1.0850m, 1.0864m, 1.0848m, 1.0858m); // High 1.0864 ≥ T1, no fill

        var plan = Manager.Decide(trade, t1Bar, new ExitContext(barClose));

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(ExitActionKind.Close);
        plan.Actions[0].Reason.Should().Be(TradeCloseReason.TimeExit);
        plan.Actions.Should().NotContain(a => a.Kind == ExitActionKind.ScaleOut);
    }

    [Fact]
    public void A_time_exit_overrides_a_same_bar_stop_trail()
    {
        // The bar is favorable enough to earn a trail rung (≥50% to T1) but past the max hold — the time-exit wins;
        // no stop is moved on the force-close bar.
        var trade = BullishTrade();
        var barClose = Open.AddMinutes(120);
        var trailBar = BarAt(barClose, 1.0845m, 1.0850m, 1.0840m, 1.0848m); // +18 pips ≈ 56% to T1, no fill

        var plan = Manager.Decide(trade, trailBar, new ExitContext(barClose));

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(ExitActionKind.Close);
        plan.Actions[0].Reason.Should().Be(TradeCloseReason.TimeExit);
        plan.Actions.Should().NotContain(a => a.Kind == ExitActionKind.MoveStop);
    }

    [Fact]
    public void The_decided_time_exit_applies_to_a_trade_and_closes_it()
    {
        var trade = BullishTrade();
        var barClose = Open.AddMinutes(120);

        Apply(trade, Manager.Decide(trade, BarAt(barClose, 1.0835m, 1.0840m, 1.0830m, 1.0838m), new ExitContext(barClose)));

        trade.Status.Should().Be(TradeStatus.Closed);
        trade.CloseReason.Should().Be(TradeCloseReason.TimeExit);
        trade.ExitPrice!.Value.Value.Should().Be(1.0838m);
    }

    [Fact]
    public void The_deferred_fx_close_boundary_is_rejected_by_validation()
    {
        var options = new ExitManagementOptions { NoOvernightBoundary = NoOvernightBoundary.NyFxClose1700 };

        options.Validate().Should().ContainMatch("*NyFxClose1700*not yet supported*");
    }

    [Fact]
    public void The_deferred_fx_close_boundary_throws_if_it_is_ever_reached()
    {
        // Belt-and-suspenders: even if an unvalidated options instance slips through, the orchestrator refuses to
        // silently apply an unimplemented boundary.
        var manager = new ExitManager(
            new FillEvaluator(new FillOptions()),
            new StopTrailPolicy(new StopTrailOptions()),
            new ExecutionCostModel(new ExecutionCostOptions()),
            new ExitManagementOptions { NoOvernightBoundary = NoOvernightBoundary.NyFxClose1700 },
            NyClock,
            new TradeStyleOptions());
        var entry = new DateTimeOffset(2024, 7, 2, 3, 30, 0, TimeSpan.Zero);
        var barClose = new DateTimeOffset(2024, 7, 2, 3, 45, 0, TimeSpan.Zero); // same NY day, < max hold
        var trade = BullishTradeOpenedAt(entry);
        var quiet = BarAt(barClose, 1.0835m, 1.0840m, 1.0830m, 1.0838m);

        var act = () => manager.Decide(trade, quiet, new ExitContext(barClose));

        act.Should().Throw<NotSupportedException>();
    }
}
