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
        Feed(ctx, detector, Candle(14, 1.0801m, 1.0826m, 1.0800m, 1.0824m)); // bullish leg, body origin (open) 1.0801

        Feed(ctx, detector, Candle(15, 1.0805m, 1.0806m, 1.0795m, 1.0797m)); // closes below the origin

        ctx.LastDisplacement!.Retraced.Should().BeTrue();
    }

    // EG-1: the leg is anchored body-to-body by default (origin = Open, terminus = Close); the candle below is a
    // bullish displacement whose body leg is 1.0800 -> 1.0810 and whose wick leg is 1.0796 -> 1.0812.
    private static readonly DateOnly NyDate = new(2024, 7, 1); // every fixture candle (07:00 UTC = 03:00 NY EDT) is this NY date.

    private static MarketContext FeedLeg(
        DisplacementOptions options, decimal open, decimal high, decimal low, decimal close, EconomicEvent? calendar = null)
    {
        var ctx = NewContext();
        var detector = new DisplacementDetector(options);
        if (calendar is { } e)
        {
            ctx.LoadCalendar([e]);
        }

        Warmup(ctx, detector, 14);
        Feed(ctx, detector, Candle(14, open, high, low, close));
        return ctx;
    }

    private static MarketContext FeedBullishLeg(DisplacementOptions options, EconomicEvent? calendar = null)
        => FeedLeg(options, 1.0800m, 1.0812m, 1.0796m, 1.0810m, calendar);

    [Fact]
    public void Body_to_body_is_the_default_anchor_open_to_close()
    {
        var leg = FeedBullishLeg(new DisplacementOptions()).LastDisplacement!;

        leg.Origin.Value.Should().Be(1.0800m);   // Open
        leg.Terminus.Value.Should().Be(1.0810m); // Close (not the 1.0812 high)
    }

    [Fact]
    public void A_bearish_body_leg_anchors_open_to_close()
    {
        // Bearish: Close 1.0800 < Open 1.0810, so the body leg runs down 1.0810 -> 1.0800.
        var leg = FeedLeg(new DisplacementOptions(), 1.0810m, 1.0812m, 1.0798m, 1.0800m).LastDisplacement!;

        leg.Direction.Should().Be(Direction.Bearish);
        leg.Origin.Value.Should().Be(1.0810m);   // Open
        leg.Terminus.Value.Should().Be(1.0800m); // Close (not the 1.0798 low)
    }

    [Fact]
    public void A_wick_to_wick_override_anchors_low_to_high()
    {
        var leg = FeedBullishLeg(new DisplacementOptions { AnchorMode = LegAnchorMode.WickToWick }).LastDisplacement!;

        leg.Origin.Value.Should().Be(1.0796m);   // Low
        leg.Terminus.Value.Should().Be(1.0812m); // High
    }

    [Fact]
    public void A_bearish_wick_to_wick_override_anchors_high_to_low()
    {
        var leg = FeedLeg(new DisplacementOptions { AnchorMode = LegAnchorMode.WickToWick }, 1.0810m, 1.0812m, 1.0798m, 1.0800m).LastDisplacement!;

        leg.Direction.Should().Be(Direction.Bearish);
        leg.Origin.Value.Should().Be(1.0812m);   // High
        leg.Terminus.Value.Should().Be(1.0798m); // Low
    }

    [Fact]
    public void The_retrace_invalidation_threshold_moves_with_the_anchor()
    {
        // InvalidateRetracedLeg keys on Origin — the body Open (1.0800) by default, the wick Low (1.0796) under
        // WickToWick. A follow-up close at 1.0798 (below the Open but above the Low) invalidates the body leg but
        // not the wick leg, so the retrace reference tracks the anchor like the OTE band and the leg equilibrium do.
        static bool RetracesAfterACloseBetweenOpenAndLow(DisplacementOptions options)
        {
            var ctx = NewContext();
            var detector = new DisplacementDetector(options);
            Warmup(ctx, detector, 14);
            Feed(ctx, detector, Candle(14, 1.0800m, 1.0812m, 1.0796m, 1.0810m)); // body origin 1.0800, wick origin 1.0796
            Feed(ctx, detector, Candle(15, 1.0799m, 1.0800m, 1.0797m, 1.0798m)); // small bar, closes 1.0798
            return ctx.LastDisplacement!.Retraced;
        }

        RetracesAfterACloseBetweenOpenAndLow(new DisplacementOptions()).Should().BeTrue();                                       // body leg invalidated
        RetracesAfterACloseBetweenOpenAndLow(new DisplacementOptions { AnchorMode = LegAnchorMode.WickToWick }).Should().BeFalse(); // wick leg survives
    }

    [Fact]
    public void An_fomc_day_flips_the_leg_back_to_wick()
    {
        var leg = FeedBullishLeg(new DisplacementOptions(), new EconomicEvent(NyDate, CalendarEventType.Fomc)).LastDisplacement!;

        leg.Origin.Value.Should().Be(1.0796m);   // wick origin, even though AnchorMode defaults to BodyToBody
        leg.Terminus.Value.Should().Be(1.0812m);
    }

    [Fact]
    public void An_nfp_day_flips_the_leg_back_to_wick()
    {
        var leg = FeedBullishLeg(new DisplacementOptions(), new EconomicEvent(NyDate, CalendarEventType.Nfp)).LastDisplacement!;

        leg.Origin.Value.Should().Be(1.0796m);
        leg.Terminus.Value.Should().Be(1.0812m);
    }

    [Fact]
    public void An_fomc_event_on_another_day_does_not_flip_the_anchor()
    {
        var leg = FeedBullishLeg(new DisplacementOptions(), new EconomicEvent(NyDate.AddDays(1), CalendarEventType.Fomc)).LastDisplacement!;

        leg.Origin.Value.Should().Be(1.0800m); // stays body — the FOMC is tomorrow
    }

    [Fact]
    public void A_cpi_event_does_not_flip_the_anchor_only_fomc_and_nfp_do()
    {
        var leg = FeedBullishLeg(new DisplacementOptions(), new EconomicEvent(NyDate, CalendarEventType.Cpi)).LastDisplacement!;

        leg.Origin.Value.Should().Be(1.0800m); // stays body — CPI is not the §2.5.1-step-7 wick exception
    }

    [Fact]
    public void Without_a_loaded_calendar_the_anchor_fails_open_to_body()
    {
        // WickAnchorOnFomcNfp is on by default, but with no calendar the exception cannot fire -> body.
        var leg = FeedBullishLeg(new DisplacementOptions()).LastDisplacement!;

        leg.Origin.Value.Should().Be(1.0800m);
    }

    [Fact]
    public void The_fomc_wick_exception_can_be_disabled()
    {
        var leg = FeedBullishLeg(
            new DisplacementOptions { WickAnchorOnFomcNfp = false },
            new EconomicEvent(NyDate, CalendarEventType.Fomc)).LastDisplacement!;

        leg.Origin.Value.Should().Be(1.0800m); // operator opted out -> stays body even on the FOMC day
    }

    [Fact]
    public void The_leg_equilibrium_moves_with_the_anchor()
    {
        // Option (b): the FVG/OB correct-half equilibrium reads the same anchored leg as the OTE entry.
        FeedBullishLeg(new DisplacementOptions()).LastDisplacement!
            .EquilibriumPrice.Should().Be(1.0805m); // (1.0800 + 1.0810) / 2 — the body midpoint
        FeedBullishLeg(new DisplacementOptions { AnchorMode = LegAnchorMode.WickToWick }).LastDisplacement!
            .EquilibriumPrice.Should().Be(1.0804m); // (1.0796 + 1.0812) / 2 — the wick midpoint
    }
}
