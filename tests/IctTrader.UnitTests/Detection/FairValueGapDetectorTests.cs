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
/// Locks the FVG rules (plan §2.3/§2.5.1 step 6) and the verifier-corrected premium/discount gate: a
/// bullish gap is a confluence only in DISCOUNT (top ≤ equilibrium), a bearish gap only in PREMIUM
/// (bottom ≥ equilibrium); plus two-touch void and full-fill mitigation.
/// </summary>
public class FairValueGapDetectorTests
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

    private static DetectorResult Feed(MarketContext context, FairValueGapDetector detector, Candle candle)
    {
        context.Append(candle);
        return detector.Detect(context, candle);
    }

    // A 3-candle bullish gap with bottom 1.0820 / top 1.0830.
    private static void FeedBullishGap(MarketContext ctx, FairValueGapDetector detector, out DetectorResult last)
    {
        Feed(ctx, detector, Candle(0, 1.0815m, 1.0820m, 1.0810m, 1.0818m));
        Feed(ctx, detector, Candle(1, 1.0828m, 1.0850m, 1.0825m, 1.0845m));
        last = Feed(ctx, detector, Candle(2, 1.0835m, 1.0860m, 1.0830m, 1.0855m));
    }

    [Fact]
    public void Bullish_gap_in_discount_emits_fvg_present()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0860m), Base)); // eq 1.0835

        FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), out var result);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        result.KeyLevel.Should().Be(1.0830m); // gap top (near edge), in discount (<= 1.0835)
        ctx.OpenFvgs.Should().ContainSingle();
    }

    [Fact]
    public void Bullish_gap_in_premium_registers_but_is_not_a_confluence()
    {
        var ctx = NewContext();
        // Equilibrium 1.0825 -> gap top 1.0830 is in PREMIUM -> never buy in premium.
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0840m), Base));

        FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), out var result);

        result.Should().Be(DetectorResult.NoMatch);
        ctx.OpenFvgs.Should().ContainSingle(); // still tracked as an array
    }

    [Fact]
    public void Bearish_gap_in_premium_emits_fvg_present()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(Direction.Bearish, Timeframe.M5, new Price(1.0860m), new Price(1.0810m), Base)); // eq 1.0835
        var detector = new FairValueGapDetector(new FvgOptions());

        Feed(ctx, detector, Candle(0, 1.0855m, 1.0860m, 1.0850m, 1.0852m));
        Feed(ctx, detector, Candle(1, 1.0843m, 1.0845m, 1.0820m, 1.0825m));
        var result = Feed(ctx, detector, Candle(2, 1.0838m, 1.0840m, 1.0815m, 1.0820m)); // gap bottom 1.0840 >= 1.0835

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bearish);
        result.KeyLevel.Should().Be(1.0840m);
    }

    [Fact]
    public void Without_a_displacement_reference_the_half_cannot_be_confirmed()
    {
        var ctx = NewContext();

        FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), out var result);

        result.Should().Be(DetectorResult.NoMatch);
        ctx.OpenFvgs.Should().ContainSingle();
    }

    [Fact]
    public void Third_retrace_into_the_gap_voids_it()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0860m), Base));
        var detector = new FairValueGapDetector(new FvgOptions());
        FeedBullishGap(ctx, detector, out _);
        var gap = ctx.OpenFvgs.Single();

        for (var i = 3; i <= 5; i++)
        {
            Feed(ctx, detector, Candle(i, 1.0826m, 1.0828m, 1.0822m, 1.0824m)); // touch [1.0820,1.0830], no full fill
        }

        gap.State.Should().Be(FvgState.VoidedTwoTouch);
    }

    [Fact]
    public void Full_fill_mitigates_the_gap()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0860m), Base));
        var detector = new FairValueGapDetector(new FvgOptions());
        FeedBullishGap(ctx, detector, out _);
        var gap = ctx.OpenFvgs.Single();

        Feed(ctx, detector, Candle(3, 1.0822m, 1.0825m, 1.0815m, 1.0818m)); // low 1.0815 <= bottom 1.0820

        gap.State.Should().Be(FvgState.Mitigated);
    }
}
