using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the §5.1 position-sizing chain: lots derive from equity × risk% over the stop distance and the
/// instrument money geometry, the size is floored DOWN to the lot step so realized risk never exceeds budget,
/// and the stop-distance floor + minimum-lot guards reject a trade the equity cannot honestly take.
/// </summary>
public class PositionSizerTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly SymbolSpec Spec = SymbolSpec.FxMajor(Eurusd);
    private static readonly ContractSpec Contract = ContractSpec.FxMajor(Eurusd);
    private static readonly Pips MinStop = new(10m);

    // entry 1.0832, stop 1.0800 -> 32-pip risk; T2 1.0920 -> 2.75R.
    private static TradePlan BullishPlan(decimal stop = 1.0800m) => new(
        Direction.Bullish, new Price(1.0832m), new Price(stop),
        new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));

    [Fact]
    public void Sizes_a_one_percent_risk_position_from_the_stop_distance()
    {
        // 1% of 10,000 = 100 risk; 32 pips * 10/pip = 320 per lot; 100/320 = 0.3125 -> floored to 0.31.
        var sizing = PositionSizer.Size(
            new Money(10_000m), new RiskPercent(1.0m), BullishPlan(), Spec, Contract, MinStop);

        sizing.StopDistance.Value.Should().Be(32m);
        sizing.Size.Lots.Should().Be(0.31m);
        sizing.RiskBudget.Amount.Should().Be(99.2m); // 0.31 * 320, at or below the 100 nominal budget
    }

    [Fact]
    public void Floors_the_size_down_to_the_lot_step_never_rounding_up()
    {
        // 1% of 11,168 = 111.68; 111.68/320 = 0.349 -> floor to 0.34 (a round-to-nearest would give 0.35).
        var sizing = PositionSizer.Size(
            new Money(11_168m), new RiskPercent(1.0m), BullishPlan(), Spec, Contract, MinStop);

        sizing.Size.Lots.Should().Be(0.34m);
    }

    [Fact]
    public void A_stop_tighter_than_the_minimum_distance_is_rejected()
    {
        // stop 1.08270 -> 5-pip distance, below the 10-pip FX floor.
        var act = () => PositionSizer.Size(
            new Money(10_000m), new RiskPercent(1.0m), BullishPlan(stop: 1.08270m), Spec, Contract, MinStop);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Equity_too_small_to_reach_the_minimum_lot_is_rejected()
    {
        // 1% of 10 = 0.10 risk -> 0.0003 lots -> floors to 0, below the 0.01 minimum lot.
        var act = () => PositionSizer.Size(
            new Money(10m), new RiskPercent(1.0m), BullishPlan(), Spec, Contract, MinStop);

        act.Should().Throw<DomainException>();
    }
}
