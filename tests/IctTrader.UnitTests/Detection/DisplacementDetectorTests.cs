using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the quantified displacement energy gate (plan §2.5.7 caveat 5): a candle is a displacement only
/// when its body dominates its range AND exceeds ATR×multiple; weak candles and the ATR warmup window are
/// rejected; the prior leg is invalidated when price closes back beyond its origin.
/// </summary>
public class DisplacementDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Candle(int i, decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, Base.AddMinutes(5 * i), open, high, low, close, 1m);

    private static DetectorResult Feed(MarketContext context, DisplacementDetector detector, Candle candle)
    {
        context.Append(candle);
        return detector.Detect(context, candle);
    }

    private static void Warmup(MarketContext context, DisplacementDetector detector, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Feed(context, detector, Candle(i, 1.0800m, 1.0803m, 1.0798m, 1.0801m)); // small, non-energetic
        }
    }

    [Fact]
    public void Energetic_up_candle_is_a_bullish_displacement_and_publishes_the_leg()
    {
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        Warmup(ctx, detector, 14);

        var result = Feed(ctx, detector, Candle(14, 1.0801m, 1.0826m, 1.0800m, 1.0824m)); // big body up

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        ctx.LastDisplacement.Should().NotBeNull();
        ctx.LastDisplacement!.Direction.Should().Be(Direction.Bullish);
        ctx.LastDisplacement.Retraced.Should().BeFalse();
    }

    [Fact]
    public void A_weak_candle_is_not_a_displacement()
    {
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        Warmup(ctx, detector, 14);

        var result = Feed(ctx, detector, Candle(14, 1.0800m, 1.0804m, 1.0797m, 1.0801m)); // small body

        result.Should().Be(DetectorResult.NoMatch);
        ctx.LastDisplacement.Should().BeNull();
    }

    [Fact]
    public void During_atr_warmup_nothing_is_detected()
    {
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());

        DetectorResult last = DetectorResult.NoMatch;
        for (var i = 0; i < 5; i++)
        {
            last = Feed(ctx, detector, Candle(i, 1.0801m, 1.0830m, 1.0800m, 1.0828m)); // would be energetic, but no ATR yet
        }

        last.Should().Be(DetectorResult.NoMatch);
        ctx.LastDisplacement.Should().BeNull();
    }

    [Fact]
    public void A_close_beyond_the_origin_retraces_the_leg()
    {
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        Warmup(ctx, detector, 14);
        Feed(ctx, detector, Candle(14, 1.0801m, 1.0826m, 1.0800m, 1.0824m)); // bullish leg, origin (low) 1.0800

        Feed(ctx, detector, Candle(15, 1.0805m, 1.0806m, 1.0795m, 1.0797m)); // closes below the origin

        ctx.LastDisplacement!.Retraced.Should().BeTrue();
    }
}
