using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the pure §2.5.9/§2.5.10 <see cref="StopTrailPolicy"/>: the two-axis ladder (entry→T1 progress + R reached vs
/// the FROZEN 1R), tightest-wins, the strictly-tighter ratchet pre-filter, and the §2.5.8 "don't trail past the bar
/// extreme" cap that Holds rather than booking an already-hit stop. The policy DECIDES; the aggregate applies.
/// </summary>
public class StopTrailPolicyTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Open = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BarTime = new(2024, 7, 1, 7, 5, 0, TimeSpan.Zero);
    private static readonly StopTrailPolicy Policy = new(new StopTrailOptions());

    // Long: entry 1.0832, stop 1.0800 (32-pip 1R), T1 1.0880 (48-pip range), runner 1.0960.
    // So 50% of T1 = +24 pips (r 0.75), +1R = +32 pips (T1 progress 0.667), 75% of T1 = +36 pips (r 1.125).
    private static PaperTrade BullishTrade(decimal t1 = 1.0880m)
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(t1), new Price(1.0960m)));
        return new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(0.30m), pipSize: 0.0001m, valuePerPip: 10m, Open);
    }

    // Short: entry 1.0870, stop 1.0910 (40-pip 1R), T1 1.0810 (60-pip range), runner 1.0750.
    private static PaperTrade BearishTrade()
    {
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0910m),
            new TargetLadder(Direction.Bearish, new Price(1.0810m), new Price(1.0750m)));
        return new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(0.30m), pipSize: 0.0001m, valuePerPip: 10m, Open);
    }

    private static Candle Bar(decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, BarTime, open, high, low, close, 1_000m);

    [Fact]
    public void A_bar_short_of_the_first_rung_holds()
    {
        var decision = Policy.Evaluate(BullishTrade(), Bar(1.0840m, 1.0850m, 1.0838m, 1.0848m)); // +18 pips, p 0.375

        decision.Outcome.Should().Be(StopTrailOutcome.Hold);
    }

    [Fact]
    public void Reaching_half_the_t1_range_tightens_to_residual_risk()
    {
        var decision = Policy.Evaluate(BullishTrade(), Bar(1.0840m, 1.0856m, 1.0840m, 1.0850m)); // +24 pips, p 0.50, r 0.75

        decision.ShouldMove.Should().BeTrue();
        decision.NewStop!.Value.Value.Should().Be(1.0824m); // entry − 0.25 × 32-pip 1R
        decision.Trigger.Should().Be(StopTrailTrigger.T1HalfResidualRisk);
    }

    [Fact]
    public void Reaching_one_r_breaks_even_independent_of_t1_progress_and_wins_tightest()
    {
        // +32 pips: T1 progress 0.667 (< 0.75) but r = 1.0 — the 1R axis fires breakeven and dominates the residual rung.
        var decision = Policy.Evaluate(BullishTrade(), Bar(1.0840m, 1.0864m, 1.0840m, 1.0858m));

        decision.NewStop!.Value.Value.Should().Be(1.0832m); // entry (breakeven), not the looser 1.0824
        decision.Trigger.Should().Be(StopTrailTrigger.BreakevenAtOneR);
    }

    [Fact]
    public void Reaching_three_quarters_of_t1_breaks_even_when_one_r_has_not_been_reached()
    {
        // Narrow T1 (24-pip range): 75% of T1 = +18 pips ⇒ r = 0.5625 (< 1.0), so the T1 axis breaks even on its own.
        var decision = Policy.Evaluate(BullishTrade(t1: 1.0856m), Bar(1.0840m, 1.0850m, 1.0840m, 1.0848m));

        decision.NewStop!.Value.Value.Should().Be(1.0832m);
        decision.Trigger.Should().Be(StopTrailTrigger.T1ThreeQuarterBreakeven);
    }

    [Fact]
    public void Re_proposing_the_same_stop_holds_the_ratchet()
    {
        var trade = BullishTrade();
        trade.MoveStop(new Price(1.0824m), BarTime); // already at the residual-risk level

        var decision = Policy.Evaluate(trade, Bar(1.0840m, 1.0856m, 1.0840m, 1.0850m)); // would re-propose 1.0824

        decision.Outcome.Should().Be(StopTrailOutcome.Hold); // not strictly tighter
    }

    [Fact]
    public void An_earned_stop_inside_the_bars_pullback_holds_rather_than_booking_an_already_hit_level()
    {
        // +48 pips earns breakeven, but the same bar pulled back THROUGH entry (low 1.0820 ≤ 1.0832).
        var decision = Policy.Evaluate(BullishTrade(), Bar(1.0840m, 1.0880m, 1.0820m, 1.0834m));

        decision.Outcome.Should().Be(StopTrailOutcome.Hold); // §2.5.8 cap — wait for a clean bar
    }

    [Fact]
    public void The_r_axis_uses_the_frozen_one_r_not_the_trailed_stop()
    {
        var trade = BullishTrade();
        trade.MoveStop(new Price(1.0824m), BarTime); // live risk now |1.0832 − 1.0824| = 8 pips

        // +16 pips: r vs the FROZEN 32-pip 1R = 0.5 (no breakeven); vs the shrunken 8-pip live stop it would be 2.0.
        var decision = Policy.Evaluate(trade, Bar(1.0840m, 1.0848m, 1.0840m, 1.0846m));

        decision.Outcome.Should().Be(StopTrailOutcome.Hold); // proves the §5.2 frozen-1R denominator
    }

    [Fact]
    public void A_short_trade_tightens_to_residual_risk_mirrored()
    {
        var decision = Policy.Evaluate(BearishTrade(), Bar(1.0855m, 1.0860m, 1.0840m, 1.0850m)); // −30 pips, p 0.50, r 0.75

        decision.NewStop!.Value.Value.Should().Be(1.0880m); // entry + 0.25 × 40-pip 1R
        decision.Trigger.Should().Be(StopTrailTrigger.T1HalfResidualRisk);
    }

    [Fact]
    public void A_short_trade_breaks_even_at_one_r_mirrored()
    {
        var decision = Policy.Evaluate(BearishTrade(), Bar(1.0855m, 1.0860m, 1.0830m, 1.0850m)); // −40 pips, r 1.0

        decision.NewStop!.Value.Value.Should().Be(1.0870m); // entry (breakeven)
        decision.Trigger.Should().Be(StopTrailTrigger.BreakevenAtOneR);
    }

    [Fact]
    public void A_short_earned_stop_inside_the_bars_pullback_holds()
    {
        // −42 pips earns breakeven, but the same bar pulled back UP through entry (high 1.0872 ≥ 1.0870).
        var decision = Policy.Evaluate(BearishTrade(), Bar(1.0855m, 1.0872m, 1.0828m, 1.0866m));

        decision.Outcome.Should().Be(StopTrailOutcome.Hold); // §2.5.8 cap, mirrored for a short
    }

    [Fact]
    public void The_tightest_capped_rung_holds_without_falling_back_to_a_looser_rung()
    {
        // +32 pips fires BOTH the residual rung (1.0824, would clear the bar low) and breakeven via 1R (1.0832).
        // Tightest-wins picks breakeven; the bar low 1.0830 caps it — the policy Holds rather than dropping back to
        // the looser-but-fillable residual stop (a deliberate "wait for a clean bar" choice, not a fall-back).
        var decision = Policy.Evaluate(BullishTrade(), Bar(1.0840m, 1.0864m, 1.0830m, 1.0834m));

        decision.Outcome.Should().Be(StopTrailOutcome.Hold);
    }

    [Fact]
    public void A_closed_trade_cannot_be_trailed()
    {
        var trade = BullishTrade();
        trade.Close(new Price(1.0960m), TradeCloseReason.TargetHit, TradeCosts.Zero, BarTime);

        var act = () => Policy.Evaluate(trade, Bar(1.0840m, 1.0856m, 1.0840m, 1.0850m));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_candle_for_a_different_symbol_is_rejected()
    {
        var foreignBar = new Candle(new Symbol("GBPUSD"), Timeframe.M5, BarTime, 1.0840m, 1.0856m, 1.0840m, 1.0850m, 1_000m);

        var act = () => Policy.Evaluate(BullishTrade(), foreignBar);

        act.Should().Throw<DomainException>();
    }
}
