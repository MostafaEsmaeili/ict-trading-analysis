using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests;

/// <summary>
/// Smoke tests proving the self-validating value objects reject invalid state (plan §3.0). Full
/// detector/aggregate coverage arrives with WP1+.
/// </summary>
public class ValueObjectTests
{
    [Fact]
    public void Price_rejects_non_positive_values()
    {
        var act = () => new Price(0m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Pips_rejects_negative_values()
    {
        var act = () => new Pips(-1m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void PriceRange_equilibrium_is_the_midpoint()
    {
        var range = new PriceRange(new Price(1.0000m), new Price(1.1000m));

        range.Equilibrium.Should().Be(1.0500m);
    }

    [Fact]
    public void Candle_requires_a_utc_open_time()
    {
        var nonUtc = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));

        var act = () => new Candle(new Symbol("EURUSD"), Timeframe.M5, nonUtc, 1m, 2m, 0.5m, 1.5m, 100m);

        act.Should().Throw<DomainException>().WithMessage("*UTC*");
    }

    [Fact]
    public void Candle_enforces_high_is_the_bar_maximum()
    {
        var openTime = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var act = () => new Candle(
            new Symbol("EURUSD"),
            Timeframe.M5,
            openTime,
            open: 1m,
            high: 1.2m,
            low: 0.5m,
            close: 1.5m,
            volume: 1m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Direction_opposite_flips_the_bias()
    {
        Direction.Bullish.Opposite().Should().Be(Direction.Bearish);
        Direction.Bearish.ToTradeDirection().Should().Be(TradeDirection.Short);
    }
}
