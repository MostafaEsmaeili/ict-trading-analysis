using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the pure intrabar fill evaluator (plan §5.2). Proves the §628 fill matrix: touch tests read the bar
/// HIGH/LOW (never close-only) so an ICT wick-sweep stops a trade out (§2.5.8); a bar that straddles both stop and
/// runner resolves to the conservative StopFirst worst-case for BOTH directions (overriding the raw OLHC path that
/// would fill a short's target first); resting orders fill at their LEVEL so a stop-out books exactly −1R and a
/// runner books the plan reward-to-risk; gap-through fills at the level (slippage worsening is the §5.4 cost
/// model). The evaluator is pure — it DECIDES, <see cref="PaperTrade.Close"/> APPLIES.
/// </summary>
public class FillEvaluatorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset OpenedAt = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BarTime = new(2024, 7, 1, 7, 5, 0, TimeSpan.Zero);
    private static readonly FillEvaluator Evaluator = new(new FillOptions());

    // Bullish: stop 1.0800 < entry 1.0832 < T1 1.0876 < runner 1.0920 (32-pip risk, 88-pip reward = 2.75R).
    private static PaperTrade BullishTrade()
        => Trade(new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m))));

    // Bearish: stop 1.0900 > entry 1.0870 > T1 1.0820 > runner 1.0790 (30-pip risk, 80-pip reward).
    private static PaperTrade BearishTrade()
        => Trade(new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0820m), new Price(1.0790m))));

    private static PaperTrade Trade(TradePlan plan)
        => new(Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
               plan, new PositionSize(0.31m), pipSize: 0.0001m, valuePerPip: 10m, OpenedAt);

    private static Candle Bar(decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, BarTime, open, high, low, close, 1_000m);

    [Fact]
    public void Long_bar_touching_neither_level_does_not_fill()
        => Evaluator.Evaluate(BullishTrade(), Bar(1.0832m, 1.0850m, 1.0820m, 1.0840m))
            .Outcome.Should().Be(FillOutcome.NoFill);

    [Fact]
    public void Long_bar_reaching_the_runner_fills_at_the_runner_level()
    {
        var decision = Evaluator.Evaluate(BullishTrade(), Bar(1.0900m, 1.0925m, 1.0890m, 1.0918m));

        decision.Outcome.Should().Be(FillOutcome.RunnerHit);
        decision.ExitPrice!.Value.Value.Should().Be(1.0920m);
        decision.CloseReason.Should().Be(TradeCloseReason.TargetHit);
    }

    [Fact]
    public void Long_bar_reaching_the_stop_fills_at_the_stop_level()
    {
        var decision = Evaluator.Evaluate(BullishTrade(), Bar(1.0820m, 1.0830m, 1.0795m, 1.0805m));

        decision.Outcome.Should().Be(FillOutcome.StopHit);
        decision.ExitPrice!.Value.Value.Should().Be(1.0800m);
        decision.CloseReason.Should().Be(TradeCloseReason.StopHit);
    }

    [Fact]
    public void Long_bar_straddling_both_resolves_to_the_stop_worst_case()
        => Evaluator.Evaluate(BullishTrade(), Bar(1.0832m, 1.0925m, 1.0795m, 1.0900m))
            .Outcome.Should().Be(FillOutcome.StopHit);

    [Fact]
    public void Short_bar_touching_neither_level_does_not_fill()
        => Evaluator.Evaluate(BearishTrade(), Bar(1.0870m, 1.0880m, 1.0850m, 1.0860m))
            .Outcome.Should().Be(FillOutcome.NoFill);

    [Fact]
    public void Short_bar_reaching_the_runner_fills_at_the_runner_level()
    {
        var decision = Evaluator.Evaluate(BearishTrade(), Bar(1.0820m, 1.0830m, 1.0785m, 1.0800m));

        decision.Outcome.Should().Be(FillOutcome.RunnerHit);
        decision.ExitPrice!.Value.Value.Should().Be(1.0790m);
        decision.CloseReason.Should().Be(TradeCloseReason.TargetHit);
    }

    [Fact]
    public void Short_bar_reaching_the_stop_fills_at_the_stop_level()
    {
        var decision = Evaluator.Evaluate(BearishTrade(), Bar(1.0880m, 1.0905m, 1.0870m, 1.0890m));

        decision.Outcome.Should().Be(FillOutcome.StopHit);
        decision.ExitPrice!.Value.Value.Should().Be(1.0900m);
        decision.CloseReason.Should().Be(TradeCloseReason.StopHit);
    }

    [Fact]
    public void Short_bar_straddling_both_resolves_to_the_stop_worst_case()
        // The decisive case: raw Open→Low→High→Close would fill the short's target (Low) first; the worst-case
        // StopFirst assumption overrides that and keeps the simulation conservative.
        => Evaluator.Evaluate(BearishTrade(), Bar(1.0870m, 1.0905m, 1.0785m, 1.0800m))
            .Outcome.Should().Be(FillOutcome.StopHit);

    [Fact]
    public void A_wick_sweep_that_closes_back_inside_still_stops_the_trade_out()
        // Low pierces the stop but the candle closes well above it — an ICT sweep. Close-only would let it survive.
        => Evaluator.Evaluate(BullishTrade(), Bar(1.0820m, 1.0840m, 1.0795m, 1.0835m))
            .Outcome.Should().Be(FillOutcome.StopHit);

    [Fact]
    public void A_bar_gapping_through_the_stop_still_fills_at_the_stop_level()
    {
        // The whole bar is at/below the stop (a gap-through). This slice fills at the LEVEL; gap-through slippage
        // (filling at the worse gapped price) is the §5.4 cost model's job, applied downstream.
        var decision = Evaluator.Evaluate(BullishTrade(), Bar(1.0795m, 1.0798m, 1.0790m, 1.0796m));

        decision.Outcome.Should().Be(FillOutcome.StopHit);
        decision.ExitPrice!.Value.Value.Should().Be(1.0800m);
    }

    [Fact]
    public void A_bar_reaching_only_the_partial_target_does_not_fill_in_this_slice()
        // High tags T1 (1.0876) but not the runner (1.0920); no partial scaling yet, so the trade stays open.
        => Evaluator.Evaluate(BullishTrade(), Bar(1.0860m, 1.0880m, 1.0855m, 1.0875m))
            .Outcome.Should().Be(FillOutcome.NoFill);

    [Fact]
    public void An_exact_kiss_of_the_stop_fills_it()
        // Inclusive boundary: a resting order at the level fills when price reaches it.
        => Evaluator.Evaluate(BullishTrade(), Bar(1.0810m, 1.0815m, 1.0800m, 1.0805m))
            .Outcome.Should().Be(FillOutcome.StopHit);

    [Fact]
    public void A_straddling_long_bar_fills_the_runner_under_the_optimistic_assumption()
    {
        var optimistic = new FillEvaluator(new FillOptions { StopVsTarget = IntrabarFillAssumption.TargetFirst });

        optimistic.Evaluate(BullishTrade(), Bar(1.0832m, 1.0925m, 1.0795m, 1.0900m))
            .Outcome.Should().Be(FillOutcome.RunnerHit);
    }

    [Fact]
    public void Evaluating_an_already_closed_trade_is_rejected()
    {
        var trade = BullishTrade();
        trade.Close(new Price(1.0800m), TradeCloseReason.StopHit, TradeCosts.Zero, BarTime);

        var act = () => Evaluator.Evaluate(trade, Bar(1.0820m, 1.0830m, 1.0795m, 1.0805m));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_candle_for_a_different_symbol_is_rejected()
    {
        var foreignBar = new Candle(
            new Symbol("GBPUSD"), Timeframe.M5, BarTime, 1.0832m, 1.0850m, 1.0820m, 1.0840m, 1_000m);

        var act = () => Evaluator.Evaluate(BullishTrade(), foreignBar);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_null_trade_or_null_options_is_rejected()
    {
        var act = () => Evaluator.Evaluate(null!, Bar(1.0832m, 1.0850m, 1.0820m, 1.0840m));
        act.Should().Throw<ArgumentNullException>();

        var nullOptions = () => new FillEvaluator(null!);
        nullOptions.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Applying_a_stop_decision_books_minus_one_r()
    {
        var trade = BullishTrade();
        var decision = Evaluator.Evaluate(trade, Bar(1.0820m, 1.0830m, 1.0795m, 1.0805m));

        trade.Close(decision.ExitPrice!.Value, decision.CloseReason!.Value, TradeCosts.Zero, BarTime);

        trade.RealizedR.Should().Be(-1m);
        trade.RealizedPnl!.Value.Amount.Should().Be(-99.2m); // 0.31 lots * 32 pips * 10/pip
    }

    [Fact]
    public void A_trailed_breakeven_stop_fills_at_the_moved_level_and_books_about_zero_r()
    {
        var trade = BullishTrade();
        trade.MoveStop(new Price(1.0832m), BarTime); // ratchet to breakeven (entry)

        var decision = Evaluator.Evaluate(trade, Bar(1.0835m, 1.0840m, 1.0830m, 1.0838m)); // low 1.0830 ≤ 1.0832

        decision.Outcome.Should().Be(FillOutcome.StopHit);
        decision.ExitPrice!.Value.Value.Should().Be(1.0832m); // the trailed level, not the original 1.0800

        trade.Close(decision.ExitPrice!.Value, decision.CloseReason!.Value, TradeCosts.Zero, BarTime);
        trade.RealizedR!.Value.Should().Be(0m);            // breakeven, not −1R
        trade.RealizedPnl!.Value.Amount.Should().Be(0m);
        trade.RiskBudget.Amount.Should().Be(99.2m);        // the reserved risk is unchanged by the trail
    }

    [Fact]
    public void A_long_stop_trailed_into_profit_books_positive_r_on_a_pullback()
    {
        var trade = BullishTrade();
        trade.MoveStop(new Price(1.0848m), BarTime); // entry + 0.5R (16 pips above entry) → profit-lock

        var decision = Evaluator.Evaluate(trade, Bar(1.0852m, 1.0855m, 1.0845m, 1.0850m)); // low 1.0845 ≤ 1.0848

        decision.Outcome.Should().Be(FillOutcome.StopHit);
        decision.ExitPrice!.Value.Value.Should().Be(1.0848m);

        trade.Close(decision.ExitPrice!.Value, decision.CloseReason!.Value, TradeCosts.Zero, BarTime);
        trade.RealizedR!.Value.Should().BeApproximately(0.5m, 0.0001m); // a pullback to the locked stop is a WIN
    }

    [Fact]
    public void A_short_breakeven_trail_fills_at_entry_for_about_zero_r()
    {
        // entry 1.0870, stop 1.0900 (30-pip 1R), runner 1.0790.
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0820m), new Price(1.0790m)));
        var trade = new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(0.30m), pipSize: 0.0001m, valuePerPip: 10m, OpenedAt);
        trade.MoveStop(new Price(1.0870m), BarTime); // short stop ratchets DOWN to breakeven (entry)

        var decision = Evaluator.Evaluate(trade, Bar(1.0866m, 1.0872m, 1.0860m, 1.0868m)); // high 1.0872 ≥ 1.0870

        decision.Outcome.Should().Be(FillOutcome.StopHit);
        decision.ExitPrice!.Value.Value.Should().Be(1.0870m);

        trade.Close(decision.ExitPrice!.Value, decision.CloseReason!.Value, TradeCosts.Zero, BarTime);
        trade.RealizedR!.Value.Should().Be(0m);
    }

    [Fact]
    public void A_trailed_quarter_risk_stop_books_about_minus_quarter_r()
    {
        var trade = BullishTrade();
        trade.MoveStop(new Price(1.0824m), BarTime); // entry − 0.25 × 32-pip 1R → 25% residual risk

        var decision = Evaluator.Evaluate(trade, Bar(1.0828m, 1.0830m, 1.0820m, 1.0826m)); // low 1.0820 ≤ 1.0824

        decision.ExitPrice!.Value.Value.Should().Be(1.0824m);

        trade.Close(decision.ExitPrice!.Value, decision.CloseReason!.Value, TradeCosts.Zero, BarTime);
        trade.RealizedR!.Value.Should().BeApproximately(-0.25m, 0.0001m);
    }

    [Fact]
    public void Applying_a_runner_decision_books_the_plan_reward_to_risk()
    {
        var trade = BullishTrade();
        var decision = Evaluator.Evaluate(trade, Bar(1.0900m, 1.0925m, 1.0890m, 1.0918m));

        trade.Close(decision.ExitPrice!.Value, decision.CloseReason!.Value, TradeCosts.Zero, BarTime);

        trade.RealizedR.Should().BeApproximately(2.75m, 0.0001m); // 88-pip reward / 32-pip risk
        trade.RealizedPnl!.Value.Amount.Should().Be(272.8m);      // 0.31 lots * 88 pips * 10/pip
    }
}
