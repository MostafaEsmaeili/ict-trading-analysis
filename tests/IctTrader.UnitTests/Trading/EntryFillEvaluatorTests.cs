using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the §2.5.1-step-7 <see cref="EntryFillEvaluator"/>: ICT enters on a resting LIMIT at the OTE/FVG level, so
/// price must RETRACE into the entry for the limit to fill (long fills when the bar trades DOWN to it, short when it
/// trades UP to it — bar High/Low touch, inclusive). A resting limit fills at the LEVEL, never the better gap price,
/// so the planned 1R equals the booked 1R. The DECIDE half (it returns an <see cref="EntryFillDecision"/> the caller
/// applies); the armed lifecycle, the no-chase cancellation, and the same-bar entry-then-stop straddle are later cuts.
/// </summary>
public class EntryFillEvaluatorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Utc = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly EntryFillEvaluator Evaluator = new(new EntryManagementOptions());

    // Long: entry 1.0832 (the OTE/FVG limit, in discount), stop 1.0800, T1 1.0876, runner 1.0920.
    private static Setup BullishSetup()
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new Setup(
            Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, plan,
            new SetupReason("Daily bias Bullish; sweep; MSS; FVG; OTE"), Utc);
    }

    // Short mirror: entry 1.0870 (in premium), stop 1.0900, T1 1.0840, runner 1.0790.
    private static Setup BearishSetup()
    {
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0840m), new Price(1.0790m)));
        return new Setup(
            Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, plan,
            new SetupReason("Daily bias Bearish; sweep; MSS; FVG; OTE"), Utc);
    }

    private static Candle Bar(decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, Utc, open, high, low, close, 1_000m);

    [Fact]
    public void A_bar_that_retraces_down_to_the_long_entry_fills_the_limit()
    {
        var decision = Evaluator.Evaluate(BullishSetup(), Bar(1.0850m, 1.0855m, 1.0830m, 1.0845m));

        decision.IsFilled.Should().BeTrue();           // Low 1.0830 ≤ entry 1.0832
        decision.Outcome.Should().Be(EntryFillOutcome.Filled);
        decision.FillPrice!.Value.Value.Should().Be(1.0832m);
    }

    [Fact]
    public void A_bar_that_stays_above_the_long_entry_does_not_fill()
    {
        var decision = Evaluator.Evaluate(BullishSetup(), Bar(1.0850m, 1.0860m, 1.0840m, 1.0855m));

        decision.IsFilled.Should().BeFalse();          // Low 1.0840 > entry 1.0832 — price never retraced in
        decision.Outcome.Should().Be(EntryFillOutcome.Hold);
        decision.FillPrice.Should().BeNull();
    }

    [Fact]
    public void An_exact_kiss_of_the_long_entry_fills_inclusively()
    {
        var decision = Evaluator.Evaluate(BullishSetup(), Bar(1.0850m, 1.0855m, 1.0832m, 1.0840m));

        decision.IsFilled.Should().BeTrue();           // Low == entry — an exact kiss fills
        decision.FillPrice!.Value.Value.Should().Be(1.0832m);
    }

    [Fact]
    public void A_long_limit_fills_at_the_level_not_the_better_gap_price()
    {
        // The bar trades well through the limit; a resting limit still fills at its LEVEL, never the lower price, so
        // the booked 1R (entry − stop) equals the planned 1R.
        var decision = Evaluator.Evaluate(BullishSetup(), Bar(1.0820m, 1.0825m, 1.0810m, 1.0815m));

        decision.IsFilled.Should().BeTrue();
        decision.FillPrice!.Value.Value.Should().Be(1.0832m); // the entry, not 1.0810
    }

    [Fact]
    public void A_bar_that_retraces_up_to_the_short_entry_fills_the_limit()
    {
        var decision = Evaluator.Evaluate(BearishSetup(), Bar(1.0850m, 1.0872m, 1.0845m, 1.0860m));

        decision.IsFilled.Should().BeTrue();           // High 1.0872 ≥ entry 1.0870
        decision.FillPrice!.Value.Value.Should().Be(1.0870m);
    }

    [Fact]
    public void A_bar_that_stays_below_the_short_entry_does_not_fill()
    {
        var decision = Evaluator.Evaluate(BearishSetup(), Bar(1.0850m, 1.0865m, 1.0840m, 1.0855m));

        decision.IsFilled.Should().BeFalse();          // High 1.0865 < entry 1.0870
        decision.Outcome.Should().Be(EntryFillOutcome.Hold);
    }

    [Fact]
    public void A_short_limit_fills_at_the_level_not_the_better_gap_price()
    {
        var decision = Evaluator.Evaluate(BearishSetup(), Bar(1.0890m, 1.0895m, 1.0880m, 1.0885m));

        decision.IsFilled.Should().BeTrue();
        decision.FillPrice!.Value.Value.Should().Be(1.0870m); // the entry, not 1.0895
    }

    [Fact]
    public void An_exact_kiss_of_the_short_entry_fills_inclusively()
    {
        var decision = Evaluator.Evaluate(BearishSetup(), Bar(1.0855m, 1.0870m, 1.0845m, 1.0860m));

        decision.IsFilled.Should().BeTrue();           // High == entry — an exact kiss fills
        decision.FillPrice!.Value.Value.Should().Be(1.0870m);
    }

    [Fact]
    public void A_candle_for_a_different_symbol_is_rejected()
    {
        var foreignBar = new Candle(new Symbol("GBPUSD"), Timeframe.M5, Utc, 1.0850m, 1.0855m, 1.0830m, 1.0845m, 1_000m);

        var act = () => Evaluator.Evaluate(BullishSetup(), foreignBar);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_null_setup_is_rejected()
    {
        var act = () => Evaluator.Evaluate(null!, Bar(1.0850m, 1.0855m, 1.0830m, 1.0845m));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void The_same_inputs_decide_an_identical_result()
    {
        var setup = BullishSetup();
        var candle = Bar(1.0850m, 1.0855m, 1.0830m, 1.0845m);

        Evaluator.Evaluate(setup, candle).Should().Be(Evaluator.Evaluate(setup, candle));
    }
}
