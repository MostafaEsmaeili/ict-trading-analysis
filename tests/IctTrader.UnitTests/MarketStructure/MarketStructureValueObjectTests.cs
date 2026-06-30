using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.MarketStructure;

/// <summary>
/// Locks the lifecycle behaviour of the market-structure arrays the detectors register (plan §2.3/§2.5):
/// FVG two-touch void + mitigation, the swing/liquidity direction convention, the displacement
/// equilibrium that drives the entry-half gate, and dealing-range premium/discount.
/// </summary>
public class MarketStructureValueObjectTests
{
    private static readonly DateTimeOffset Utc = new(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static FairValueGap BullishFvg() =>
        new(Direction.Bullish, Timeframe.M5, new Price(1.0832m), new Price(1.0840m), Utc);

    [Fact]
    public void Breaching_a_swing_stamps_the_breaching_candle_for_same_candle_mss_recognition()
    {
        var swing = new SwingPoint(SwingKind.High, Timeframe.M5, new Price(1.0900m), Utc);
        swing.BreachedAtUtc.Should().BeNull();

        swing.Breach(Utc);

        swing.State.Should().Be(SwingState.Breached);
        swing.IsActive.Should().BeFalse();
        swing.WasBreachedOn(Utc).Should().BeTrue();
        swing.WasBreachedOn(Utc.AddMinutes(5)).Should().BeFalse(); // a different candle did not breach it
    }

    [Fact]
    public void Consequent_encroachment_is_the_fifty_percent_midpoint_of_the_gap()
    {
        // CE (ICT consequent encroachment) = the 50% of the FVG = (High + Low) / 2 — the canonical shallow entry.
        var fvg = BullishFvg(); // [1.0832, 1.0840]
        fvg.ConsequentEncroachment.Should().Be(1.0836m);
        fvg.ConsequentEncroachment.Should().Be((fvg.Top.Value + fvg.Bottom.Value) / 2m);
        fvg.ConsequentEncroachment.Should().Be(fvg.Midpoint); // CE == the gap midpoint by construction
    }

    [Fact]
    public void Near_edge_is_the_proximal_edge_and_shallower_than_the_midpoint()
    {
        // The near edge is the FIRST level price taps on the retrace: a bullish gap's TOP, a bearish gap's BOTTOM.
        var bullish = BullishFvg(); // [1.0832, 1.0840]
        bullish.NearEdge.Should().Be(1.0840m);                 // = Top
        bullish.NearEdge.Should().BeGreaterThan(bullish.Midpoint); // shallower (higher = less retrace) for a long

        var bearish = new FairValueGap(
            Direction.Bearish, Timeframe.M5, new Price(1.0832m), new Price(1.0840m), Utc);
        bearish.NearEdge.Should().Be(1.0832m);                 // = Bottom
        bearish.NearEdge.Should().BeLessThan(bearish.Midpoint);   // shallower (lower = less retrace) for a short
    }

    [Fact]
    public void Fvg_voids_on_the_configured_touch_count_and_can_mitigate()
    {
        var fvg = BullishFvg();
        fvg.Midpoint.Should().Be(1.0836m);

        fvg.RegisterTouch(voidOnTouchCount: 3);
        fvg.RegisterTouch(voidOnTouchCount: 3);
        fvg.IsOpen.Should().BeTrue();
        fvg.RegisterTouch(voidOnTouchCount: 3);

        fvg.IsOpen.Should().BeFalse();
        fvg.State.Should().Be(FvgState.VoidedTwoTouch);
    }

    [Fact]
    public void Mitigated_fvg_ignores_further_touches()
    {
        var fvg = BullishFvg();
        fvg.Mitigate();
        fvg.State.Should().Be(FvgState.Mitigated);

        fvg.RegisterTouch(voidOnTouchCount: 3);

        fvg.TouchCount.Should().Be(0);
        fvg.State.Should().Be(FvgState.Mitigated);
    }

    [Fact]
    public void Swing_high_is_buy_side_and_enables_a_bearish_trade()
    {
        var high = new SwingPoint(SwingKind.High, Timeframe.M15, new Price(1.0900m), Utc);
        var low = new SwingPoint(SwingKind.Low, Timeframe.M15, new Price(1.0800m), Utc);

        high.EnablesDirection.Should().Be(Direction.Bearish);
        low.EnablesDirection.Should().Be(Direction.Bullish);

        high.MarkConsumed();
        high.State.Should().Be(SwingState.Consumed);
    }

    [Fact]
    public void Liquidity_run_is_not_a_sweep_and_marks_do_not_fade()
    {
        var buySide = new LiquidityPool(LiquiditySide.BuySide, new Price(1.0900m), strength: 2, Utc);
        buySide.EnablesDirection.Should().Be(Direction.Bearish);

        buySide.MarkRun();

        buySide.Untapped.Should().BeFalse();
        buySide.Consumption.Should().Be(LiquidityConsumption.Run);
    }

    [Fact]
    public void Displacement_equilibrium_is_the_midpoint_of_the_leg()
    {
        var leg = new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), Utc);

        leg.Size.Should().Be(0.0100m);
        leg.EquilibriumPrice.Should().Be(1.0850m);
    }

    [Fact]
    public void Dealing_range_splits_premium_from_discount_and_re_anchors()
    {
        var range = new DealingRange(new Price(1.0800m), new Price(1.0900m), Utc);

        range.PositionPercent(new Price(1.0825m)).Should().Be(25m);
        range.IsDiscount(new Price(1.0825m), equilibriumFib: 0.50m).Should().BeTrue();
        range.IsPremium(new Price(1.0875m), equilibriumFib: 0.50m).Should().BeTrue();

        range.Reanchor(new Price(1.0850m), new Price(1.0950m), Utc);
        range.Low.Value.Should().Be(1.0850m);
        range.High.Value.Should().Be(1.0950m);
    }

    [Fact]
    public void Order_block_mean_threshold_is_the_body_midpoint_and_inverts()
    {
        // Anchor open 1.0820 (body high), close 1.0814 (body low) -> body mid 1.0817, distinct from the zone
        // range mid 1.0820 (Low 1.0810 + (High 1.0830 - Low) * 0.5) -> mean-threshold keys on the BODY.
        var ob = new OrderBlock(
            Direction.Bullish, Timeframe.M5, new Price(1.0820m), new Price(1.0830m), new Price(1.0810m),
            new Price(1.0814m), new Price(1.0820m), Utc);

        ob.MeanThreshold(0.50m).Should().Be(1.0817m);
        ob.MeanThreshold(0.50m).Should().NotBe(1.0820m); // a regression to range-based fails here

        ob.Invert();
        ob.State.Should().Be(OrderBlockState.Inverted);
    }

    [Fact]
    public void Fvg_register_touch_rejects_a_non_positive_void_count()
    {
        var act = () => BullishFvg().RegisterTouch(voidOnTouchCount: 0);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Order_block_mean_threshold_rejects_a_fraction_outside_the_unit_interval()
    {
        var ob = new OrderBlock(
            Direction.Bullish, Timeframe.M5, new Price(1.0820m), new Price(1.0830m), new Price(1.0810m),
            new Price(1.0814m), new Price(1.0820m), Utc);

        var act = () => ob.MeanThreshold(1.5m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Order_block_rejects_an_open_that_is_not_a_body_extreme()
    {
        // open 1.0817 sits strictly INSIDE the body [1.0814, 1.0820] — it is not the anchor candle's open/close
        // edge, so it cannot be a real anchor open. The within-range check alone would have let it through.
        var act = () => new OrderBlock(
            Direction.Bullish, Timeframe.M5, new Price(1.0817m), new Price(1.0830m), new Price(1.0810m),
            new Price(1.0814m), new Price(1.0820m), Utc);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Dealing_range_equilibrium_rejects_a_fib_outside_the_unit_interval()
    {
        var range = new DealingRange(new Price(1.0800m), new Price(1.0900m), Utc);

        var act = () => range.Equilibrium(1.5m);

        act.Should().Throw<DomainException>();
    }
}
