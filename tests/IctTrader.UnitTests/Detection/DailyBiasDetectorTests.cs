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
/// Locks the daily-bias read (plan §2.5.1 step 1, §2.5.10): the current price in DISCOUNT of the dealing range
/// is a bullish bias, in PREMIUM a bearish bias, exactly at the 50% equilibrium (or with no range) is NEUTRAL —
/// no trade. The §2.5.10 consecutive-close corroboration is off by default and gates the match when enabled.
/// </summary>
public class DailyBiasDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Candle(int i, decimal close, decimal? open = null)
    {
        var o = open ?? close;
        return new(Eurusd, Timeframe.M5, Base.AddMinutes(5 * i), o,
            Math.Max(o, close) + 0.0005m, Math.Min(o, close) - 0.0005m, close, 1m);
    }

    private static DealingRange Range() => new(new Price(1.0800m), new Price(1.0900m), Base); // EQ 1.0850

    private static readonly DailyBiasDetector Detector = new(new DailyBiasOptions());

    [Fact]
    public void Price_in_discount_is_a_bullish_bias()
    {
        var ctx = NewContext();
        ctx.SetDailyRange(Range());
        var candle = Candle(0, 1.0820m); // 20% of range -> discount
        ctx.Append(candle);

        var result = Detector.Detect(ctx, candle);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        ctx.Bias.Should().Be(Direction.Bullish);
    }

    [Fact]
    public void Price_in_premium_is_a_bearish_bias()
    {
        var ctx = NewContext();
        ctx.SetDailyRange(Range());
        var candle = Candle(0, 1.0880m); // 80% -> premium
        ctx.Append(candle);

        var result = Detector.Detect(ctx, candle);

        result.Direction.Should().Be(Direction.Bearish);
        ctx.Bias.Should().Be(Direction.Bearish);
    }

    [Fact]
    public void Price_exactly_at_equilibrium_is_neutral_no_trade()
    {
        var ctx = NewContext();
        ctx.SetDailyRange(Range());
        var candle = Candle(0, 1.0850m); // 50% -> equilibrium

        Detector.Detect(ctx, candle).Should().Be(DetectorResult.NoMatch);
        ctx.Bias.Should().BeNull();
    }

    [Fact]
    public void Without_a_dealing_range_the_bias_is_neutral()
    {
        var ctx = NewContext();
        var candle = Candle(0, 1.0820m);

        Detector.Detect(ctx, candle).Should().Be(DetectorResult.NoMatch);
        ctx.Bias.Should().BeNull();
    }

    [Fact]
    public void Consecutive_close_confirmation_when_enabled_requires_directional_closes()
    {
        var detector = new DailyBiasDetector(
            new DailyBiasOptions { RequireConsecutiveCloseConfirmation = true, ConsecutiveCloseCount = 3 });

        var confirmed = NewContext();
        confirmed.SetDailyRange(Range());
        for (var i = 0; i < 3; i++)
        {
            confirmed.Append(Candle(i, close: 1.0820m, open: 1.0815m)); // up-closes in discount
        }
        detector.Detect(confirmed, Candle(2, close: 1.0820m, open: 1.0815m)).Matched.Should().BeTrue();

        var broken = NewContext();
        broken.SetDailyRange(Range());
        broken.Append(Candle(0, close: 1.0820m, open: 1.0815m));
        broken.Append(Candle(1, close: 1.0820m, open: 1.0815m));
        var down = Candle(2, close: 1.0818m, open: 1.0825m); // a down-close breaks the corroboration
        broken.Append(down);
        detector.Detect(broken, down).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void Default_configuration_validates_clean()
        => new DailyBiasOptions().Validate().Should().BeEmpty();
}
