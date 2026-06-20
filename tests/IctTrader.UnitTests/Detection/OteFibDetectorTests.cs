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
/// Locks the OTE detector (plan §2.5.1 step 7): the 62–79% band is projected onto the pre-validated displacement
/// leg, and a match needs a same-direction, same-timeframe FVG/OB key level inside the band (nearest the 70.5%
/// sweet spot). No overlapping array, a fully retraced leg, or no leg all yield no match.
/// </summary>
public class OteFibDetectorTests
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

    private static readonly OteFibDetector Detector = new(new OteOptions());

    // A bullish leg 1.0800 -> 1.0900: OTE band is ~[1.0821 (79%), 1.0838 (62%)], sweet spot ~1.08295.
    private static Displacement BullishLeg() =>
        new(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), Base);

    [Fact]
    public void An_fvg_inside_the_ote_band_on_the_leg_is_an_ote_entry()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0825m), new Price(1.0835m), Base)); // midpoint 1.0830 in band

        var result = Detector.Detect(ctx, Candle());

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        result.KeyLevel.Should().Be(1.0830m);
    }

    [Fact]
    public void An_array_outside_the_band_does_not_form_an_ote()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0805m), new Price(1.0810m), Base)); // midpoint 1.08075 below the band

        Detector.Detect(ctx, Candle()).Should().Be(DetectorResult.NoMatch); // OteSkippedNoOverlap
    }

    [Fact]
    public void A_fully_retraced_leg_voids_the_ote()
    {
        var ctx = NewContext();
        var leg = BullishLeg();
        leg.MarkRetraced();
        ctx.SetDisplacement(leg);
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0825m), new Price(1.0835m), Base));

        Detector.Detect(ctx, Candle()).Should().Be(DetectorResult.NoMatch); // OteVoidedOnFullRetrace
    }

    [Fact]
    public void Without_a_displacement_leg_there_is_no_ote()
    {
        var ctx = NewContext();
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0825m), new Price(1.0835m), Base));

        Detector.Detect(ctx, Candle()).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void Default_configuration_validates_clean()
        => new OteOptions().Validate().Should().BeEmpty();

    [Fact]
    public void The_ep41_variant_needs_a_compatible_sweet_spot()
        => new OteOptions { UseEp41Variant = true }.Validate().Should().NotBeEmpty(); // 0.705 falls outside [0.62, 0.70]
}
