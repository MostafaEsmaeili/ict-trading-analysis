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
/// Locks the MSS rules (plan §2.5.1 step 5, §2.5.10): only an energetic displacement that follows a
/// same-direction sweep and CLOSES beyond a prior swing by at least the minimum is a shift; no precedent
/// sweep, a wrong-direction sweep, a weak break, or a non-displacement candle all yield no match.
/// </summary>
public class MarketStructureShiftDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Candle(decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, Base, open, high, low, close, 1m);

    private static readonly MarketStructureShiftDetector Detector = new(new MarketStructureShiftOptions());

    // Arranges a bullish setup: a swing high to break, the current displacement candle, and a recent
    // same-direction sweep. Returns the appended current candle.
    private static Candle ArrangeBullish(MarketContext ctx, decimal closeAboveSwing, bool withSweep = true)
    {
        ctx.RegisterSwingPoint(new SwingPoint(SwingKind.High, Timeframe.M5, new Price(1.0900m), Base));
        var current = Candle(1.0895m, closeAboveSwing + 0.0005m, 1.0890m, closeAboveSwing);
        ctx.Append(current);
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0890m), new Price(closeAboveSwing + 0.0005m), Base));
        if (withSweep)
        {
            ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0850m, Base, ctx.BarsProcessed));
        }

        return current;
    }

    [Fact]
    public void A_displacement_breaking_a_swing_after_a_sweep_is_a_bullish_mss()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, closeAboveSwing: 1.0920m);

        var result = Detector.Detect(ctx, current);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        ctx.LastMss.Should().NotBeNull();
        ctx.LastMss!.IsConfirmed.Should().BeTrue();
        ctx.SwingPoints.Single().State.Should().Be(SwingState.Breached);
    }

    [Fact]
    public void No_precedent_sweep_means_no_shift()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, closeAboveSwing: 1.0920m, withSweep: false);

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_wrong_direction_sweep_does_not_satisfy_the_precedent()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, closeAboveSwing: 1.0920m, withSweep: false);
        ctx.SetSweep(new SweepRecord(Direction.Bearish, 1.0950m, Base, ctx.BarsProcessed));

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_weak_break_below_the_minimum_is_rejected()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, closeAboveSwing: 1.09005m); // only 0.5 pip beyond 1.0900

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_non_displacement_candle_is_never_an_mss()
    {
        var ctx = NewContext();
        ctx.RegisterSwingPoint(new SwingPoint(SwingKind.High, Timeframe.M5, new Price(1.0900m), Base));
        var current = Candle(1.0895m, 1.0925m, 1.0890m, 1.0920m);
        ctx.Append(current);
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0850m, Base, ctx.BarsProcessed));
        // no displacement set for the current candle

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_consumed_swing_is_stale_structure_and_is_not_re_selected()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, closeAboveSwing: 1.0920m);
        ctx.SwingPoints.Single().MarkConsumed(); // already swept -> no longer live structure to break

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }
}
