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
    public void Order_block_mean_threshold_and_inversion()
    {
        var ob = new OrderBlock(Direction.Bullish, Timeframe.M5, new Price(1.0820m), new Price(1.0830m), new Price(1.0810m), Utc);

        ob.MeanThreshold(0.50m).Should().Be(1.0820m);

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
        var ob = new OrderBlock(Direction.Bullish, Timeframe.M5, new Price(1.0820m), new Price(1.0830m), new Price(1.0810m), Utc);

        var act = () => ob.MeanThreshold(1.5m);

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
