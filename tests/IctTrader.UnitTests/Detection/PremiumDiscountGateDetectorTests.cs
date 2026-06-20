using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the premium/discount entry-half veto (plan §2.5.1 step 6, §2.5.10): a discount price allows only a long,
/// a premium price only a short, and exactly-at-equilibrium emits a non-directional match when inclusive (allowed
/// both sides) or no match when not. With no dealing range there is no half to gate on.
/// </summary>
public class PremiumDiscountGateDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Candle(decimal close)
        => new(Eurusd, Timeframe.M5, Base, close, close + 0.0005m, close - 0.0005m, close, 1m);

    private static DealingRange Range() => new(new Price(1.0800m), new Price(1.0900m), Base); // EQ 1.0850

    private static readonly PremiumDiscountGateDetector Detector = new(new PremiumDiscountOptions());

    [Fact]
    public void A_discount_price_allows_only_a_long()
    {
        var ctx = NewContext();
        ctx.SetDailyRange(Range());

        var result = Detector.Detect(ctx, Candle(1.0820m)); // discount

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
    }

    [Fact]
    public void A_premium_price_allows_only_a_short()
    {
        var ctx = NewContext();
        ctx.SetDailyRange(Range());

        Detector.Detect(ctx, Candle(1.0880m)).Direction.Should().Be(Direction.Bearish); // premium
    }

    [Fact]
    public void Exactly_at_equilibrium_is_inclusive_and_non_directional_by_default()
    {
        var ctx = NewContext();
        ctx.SetDailyRange(Range());

        var result = Detector.Detect(ctx, Candle(1.0850m)); // 50%

        result.Matched.Should().BeTrue();
        result.Direction.Should().BeNull(); // allowed both sides -> no directional veto
    }

    [Fact]
    public void Exactly_at_equilibrium_is_no_match_when_not_inclusive()
    {
        var detector = new PremiumDiscountGateDetector(new PremiumDiscountOptions { InclusiveAtEquilibrium = false });
        var ctx = NewContext();
        ctx.SetDailyRange(Range());

        detector.Detect(ctx, Candle(1.0850m)).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void Without_a_dealing_range_there_is_no_gate()
    {
        var ctx = NewContext();

        Detector.Detect(ctx, Candle(1.0820m)).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void Default_configuration_validates_clean()
        => new PremiumDiscountOptions().Validate().Should().BeEmpty();
}
