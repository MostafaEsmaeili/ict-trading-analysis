using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the 3-candle fractal (plan §2.5.1 step 5): a centred pivot becomes a swing only when strictly
/// beyond both neighbours (equal highs/lows are liquidity, not swings), a swing high enables a bearish
/// trade, and a close beyond a swing breaches it (ITH/ITL invalidation).
/// </summary>
public class SwingPointDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(TimeProvider.System), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Candle(int minuteOffset, decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero).AddMinutes(minuteOffset),
            open, high, low, close, 1m);

    private static DetectorResult Feed(MarketContext context, SwingPointDetector detector, Candle candle)
    {
        context.Append(candle);
        return detector.Detect(context, candle);
    }

    [Fact]
    public void Detects_a_swing_high_and_enables_a_bearish_trade()
    {
        var ctx = NewContext();
        var detector = new SwingPointDetector(new SwingOptions());

        Feed(ctx, detector, Candle(0, 1.0840m, 1.0850m, 1.0830m, 1.0845m));
        Feed(ctx, detector, Candle(5, 1.0855m, 1.0870m, 1.0850m, 1.0865m)); // pivot — highest high
        var result = Feed(ctx, detector, Candle(10, 1.0860m, 1.0860m, 1.0840m, 1.0845m));

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bearish);
        result.KeyLevel.Should().Be(1.0870m);
        ctx.SwingPoints.Should().ContainSingle(s => s.Kind == SwingKind.High && s.Price.Value == 1.0870m);
    }

    [Fact]
    public void Detects_a_swing_low_and_enables_a_bullish_trade()
    {
        var ctx = NewContext();
        var detector = new SwingPointDetector(new SwingOptions());

        Feed(ctx, detector, Candle(0, 1.0855m, 1.0860m, 1.0840m, 1.0850m));
        Feed(ctx, detector, Candle(5, 1.0845m, 1.0850m, 1.0820m, 1.0835m)); // pivot — lowest low
        var result = Feed(ctx, detector, Candle(10, 1.0840m, 1.0855m, 1.0835m, 1.0850m));

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        result.KeyLevel.Should().Be(1.0820m);
        ctx.SwingPoints.Should().ContainSingle(s => s.Kind == SwingKind.Low && s.Price.Value == 1.0820m);
    }

    [Fact]
    public void Equal_highs_are_liquidity_not_a_swing_under_strict_inequality()
    {
        var ctx = NewContext();
        var detector = new SwingPointDetector(new SwingOptions());

        Feed(ctx, detector, Candle(0, 1.0850m, 1.0870m, 1.0840m, 1.0860m));
        Feed(ctx, detector, Candle(5, 1.0860m, 1.0870m, 1.0850m, 1.0865m)); // equal high to its neighbour
        var result = Feed(ctx, detector, Candle(10, 1.0855m, 1.0860m, 1.0840m, 1.0845m));

        result.Matched.Should().BeFalse();
        ctx.SwingPoints.Should().BeEmpty();
    }

    [Fact]
    public void Returns_no_match_until_the_window_holds_a_full_fractal()
    {
        var ctx = NewContext();
        var detector = new SwingPointDetector(new SwingOptions());

        Feed(ctx, detector, Candle(0, 1.0840m, 1.0850m, 1.0830m, 1.0845m));
        var result = Feed(ctx, detector, Candle(5, 1.0855m, 1.0870m, 1.0850m, 1.0865m));

        result.Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_close_beyond_a_swing_high_breaches_it()
    {
        var ctx = NewContext();
        var detector = new SwingPointDetector(new SwingOptions());

        Feed(ctx, detector, Candle(0, 1.0840m, 1.0850m, 1.0830m, 1.0845m));
        Feed(ctx, detector, Candle(5, 1.0855m, 1.0870m, 1.0850m, 1.0865m));
        Feed(ctx, detector, Candle(10, 1.0860m, 1.0860m, 1.0840m, 1.0845m));
        var swingHigh = ctx.SwingPoints.Single(s => s.Kind == SwingKind.High);

        Feed(ctx, detector, Candle(15, 1.0865m, 1.0885m, 1.0860m, 1.0880m)); // closes above 1.0870

        swingHigh.State.Should().Be(SwingState.Breached);
    }
}
