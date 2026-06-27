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
/// Locks the OPTIONAL <see cref="ConfluenceCondition.CleanPriceAction"/> emitter (0.40). It operationalises the
/// §2.5.6/§2.5.8 high-resistance-vs-low-resistance ("clean") concept deterministically: the displacement leg is CLEAN
/// (a low-resistance run, not an HRLR chop) when its net body-to-range ratio Σ|body| / Σrange over the leg candles is
/// at or above <see cref="CleanPriceActionOptions.CleanBodyRatio"/> (default 0.6, INVENTED-flagged). It reads the leg
/// from <see cref="MarketContext.LastDisplacement"/> (OriginAtUtc/AtUtc/LegBars) and emits the bias direction.
/// </summary>
public class CleanPriceActionDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero));
    private static readonly DateTimeOffset London = new(2024, 7, 1, 6, 30, 0, TimeSpan.Zero);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Bar(DateTimeOffset openUtc, decimal open, decimal high, decimal low, decimal close) =>
        new(Eurusd, Timeframe.M5, openUtc, open, high, low, close, 1m);

    /// <summary>Builds a context with a bullish leg of the given bars + the bias set, and runs the detector.</summary>
    private static DetectorResult Detect(
        CleanPriceActionOptions options, Direction? bias, IReadOnlyList<Candle> leg, bool registerDisplacement = true)
    {
        var ctx = NewContext();
        foreach (var bar in leg)
        {
            ctx.Append(bar);
        }

        ctx.SetBias(bias);
        if (registerDisplacement)
        {
            var first = leg[0];
            var last = leg[^1];
            ctx.SetDisplacement(new Displacement(
                Direction.Bullish, Timeframe.M5, new Price(first.Open), new Price(last.Close),
                last.OpenTimeUtc, first.OpenTimeUtc, leg.Count));
        }

        return new CleanPriceActionDetector(options).Detect(ctx, leg[^1]);
    }

    // A single big full-body bar: body 0.0040, range 0.0040 -> ratio 1.0 (clean).
    private static IReadOnlyList<Candle> CleanLeg() =>
    [
        Bar(London, 1.0800m, 1.0840m, 1.0800m, 1.0840m),
    ];

    // A single doji-ish bar: body 0.0002, range 0.0042 -> ratio ~0.048 (choppy, high resistance).
    private static IReadOnlyList<Candle> ChoppyLeg() =>
    [
        Bar(London, 1.0820m, 1.0842m, 1.0800m, 1.0822m),
    ];

    [Fact]
    public void A_clean_full_body_leg_matches_the_bias_direction()
    {
        var result = Detect(new CleanPriceActionOptions(), Direction.Bullish, CleanLeg());

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
    }

    [Fact]
    public void A_choppy_wicky_leg_does_not_match()
        => Detect(new CleanPriceActionOptions(), Direction.Bullish, ChoppyLeg()).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void The_boundary_ratio_is_inclusive()
    {
        // A two-bar leg with Σ|body| = 0.0030 and Σrange = 0.0050 -> ratio exactly 0.60, the inclusive threshold.
        var b1 = Bar(London, 1.0800m, 1.0825m, 1.0800m, 1.0820m);                 // body 0.0020, range 0.0025
        var b2 = Bar(London.AddMinutes(5), 1.0820m, 1.0835m, 1.0815m, 1.0830m);   // body 0.0010, range 0.0020
        // Σ|body| = 0.0030, Σrange = 0.0045 -> 0.6667; tweak to hit 0.60 exactly:
        var c1 = Bar(London, 1.0800m, 1.0830m, 1.0800m, 1.0820m);                 // body 0.0020, range 0.0030
        var c2 = Bar(London.AddMinutes(5), 1.0820m, 1.0840m, 1.0820m, 1.0830m);   // body 0.0010, range 0.0020
        // Σ|body| = 0.0030, Σrange = 0.0050 -> exactly 0.60.
        Detect(new CleanPriceActionOptions { CleanBodyRatio = 0.60m }, Direction.Bullish, [c1, c2])
            .Matched.Should().BeTrue();
        _ = (b1, b2);
    }

    [Fact]
    public void A_neutral_bias_yields_no_match()
        => Detect(new CleanPriceActionOptions(), bias: null, CleanLeg()).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void No_displacement_leg_yields_no_match()
        => Detect(new CleanPriceActionOptions(), Direction.Bullish, CleanLeg(), registerDisplacement: false)
            .Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void A_disabled_detector_never_matches()
        => Detect(new CleanPriceActionOptions { Enabled = false }, Direction.Bullish, CleanLeg())
            .Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void Default_configuration_validates_clean()
        => new CleanPriceActionOptions().Validate().Should().BeEmpty();

    [Fact]
    public void A_ratio_outside_the_open_unit_interval_is_rejected()
    {
        new CleanPriceActionOptions { CleanBodyRatio = 0m }.Validate().Should().NotBeEmpty();
        new CleanPriceActionOptions { CleanBodyRatio = 1.0m }.Validate().Should().BeEmpty();   // 1.0 = all-body, valid
        new CleanPriceActionOptions { CleanBodyRatio = 1.1m }.Validate().Should().NotBeEmpty();
    }
}
