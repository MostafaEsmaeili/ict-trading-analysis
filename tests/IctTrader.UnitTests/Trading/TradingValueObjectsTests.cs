using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the paper-trade money geometry (plan §5.1/§5.4): <see cref="Money"/> is sign-agnostic with the
/// expected arithmetic, a <see cref="PositionSize"/> is strictly positive, and a <see cref="ContractSpec"/>
/// self-validates the value-per-pip / lot-step / minimum-lot it feeds the sizer.
/// </summary>
public class TradingValueObjectsTests
{
    private static readonly Symbol Eurusd = new("EURUSD");

    [Fact]
    public void Money_arithmetic_and_sign_behave()
    {
        (new Money(100m) + new Money(50m)).Amount.Should().Be(150m);
        (new Money(100m) - new Money(150m)).Amount.Should().Be(-50m);
        (new Money(3.1m) * 88m).Amount.Should().Be(272.8m);
        Money.Zero.Amount.Should().Be(0m);
        new Money(-0.01m).IsNegative.Should().BeTrue();
        new Money(0.01m).IsPositive.Should().BeTrue();
        (new Money(99.2m) <= new Money(100m)).Should().BeTrue();
        (new Money(500m) > new Money(499.99m)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void A_non_positive_position_size_is_rejected(decimal lots)
    {
        var act = () => new PositionSize(lots);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void The_fx_major_contract_spec_carries_the_documented_defaults()
    {
        var spec = ContractSpec.FxMajor(Eurusd);

        spec.ValuePerPip.Should().Be(10m);
        spec.LotStep.Should().Be(0.01m);
        spec.MinLot.Should().Be(0.01m);
    }

    [Fact]
    public void A_contract_spec_rejects_non_positive_or_inconsistent_geometry()
    {
        var act1 = () => new ContractSpec(Eurusd, valuePerPip: 0m, lotStep: 0.01m, minLot: 0.01m);
        var act2 = () => new ContractSpec(Eurusd, valuePerPip: 10m, lotStep: 0m, minLot: 0.01m);
        var act3 = () => new ContractSpec(Eurusd, valuePerPip: 10m, lotStep: 0.01m, minLot: 0m);
        var act4 = () => new ContractSpec(Eurusd, valuePerPip: 10m, lotStep: 0.1m, minLot: 0.01m); // min < step

        act1.Should().Throw<DomainException>();
        act2.Should().Throw<DomainException>();
        act3.Should().Throw<DomainException>();
        act4.Should().Throw<DomainException>();
    }
}
