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
/// Locks the dealing-range anchoring (plan §2.5.1 step 1, §2.5.10): the context detector frames the range from
/// the lowest active swing-low to the highest active swing-high, re-anchors (expands) when a new swing breaks
/// beyond it, and produces nothing until both a high and a low exist. It is non-scoring (no confluence).
/// </summary>
public class DealingRangeContextDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Candle()
        => new(Eurusd, Timeframe.M5, Base, 1.0850m, 1.0855m, 1.0845m, 1.0850m, 1m);

    private static SwingPoint High(decimal price) => new(SwingKind.High, Timeframe.M5, new Price(price), Base);

    private static SwingPoint Low(decimal price) => new(SwingKind.Low, Timeframe.M5, new Price(price), Base);

    private static readonly DealingRangeContextDetector Detector = new(new PremiumDiscountOptions());

    [Fact]
    public void It_is_a_non_scoring_context_provider()
        => Detector.Condition.Should().BeNull();

    [Fact]
    public void Anchors_the_range_from_the_active_swing_extremes()
    {
        var ctx = NewContext();
        ctx.RegisterSwingPoint(High(1.0900m));
        ctx.RegisterSwingPoint(Low(1.0800m));

        Detector.Detect(ctx, Candle());

        ctx.DailyRange.Should().NotBeNull();
        ctx.DailyRange!.Low.Value.Should().Be(1.0800m);
        ctx.DailyRange.High.Value.Should().Be(1.0900m);
    }

    [Fact]
    public void Re_anchors_by_expanding_when_a_new_swing_breaks_beyond_the_range()
    {
        var ctx = NewContext();
        ctx.RegisterSwingPoint(High(1.0900m));
        ctx.RegisterSwingPoint(Low(1.0800m));
        Detector.Detect(ctx, Candle());

        ctx.RegisterSwingPoint(High(1.0930m)); // a higher high expands the range
        Detector.Detect(ctx, Candle());

        ctx.DailyRange!.High.Value.Should().Be(1.0930m);
        ctx.DailyRange.Low.Value.Should().Be(1.0800m);
    }

    [Fact]
    public void Without_both_a_high_and_a_low_no_range_is_anchored()
    {
        var ctx = NewContext();
        ctx.RegisterSwingPoint(High(1.0900m)); // only a high

        Detector.Detect(ctx, Candle()).Should().Be(DetectorResult.NoMatch);
        ctx.DailyRange.Should().BeNull();
    }
}
