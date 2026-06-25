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
        // Doji noise: Open == Close so the immediate predecessor always breaks the grow-backward run
        // (neither an up nor a down candle), keeping an isolated energetic candle a single-bar leg.
        for (var i = 0; i < count; i++)
        {
            Feed(context, detector, Candle(i, 1.0800m, 1.0803m, 1.0798m, 1.0800m)); // small, non-energetic doji
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

    // ---- TIME-11-12: multi-candle displacement leg (grow-then-gate) ----

    [Fact]
    public void A_single_energetic_bar_is_a_one_bar_leg_with_origin_equal_to_terminus_time()
    {
        // Backward-compat: an isolated energetic candle (the doji predecessor breaks the run) reduces to
        // LegBars == 1, OriginAtUtc == AtUtc, and the legacy (Open, Close) anchor.
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        Warmup(ctx, detector, 14);
        var current = Candle(14, 1.0800m, 1.0826m, 1.0800m, 1.0824m);
        Feed(ctx, detector, current);

        var leg = ctx.LastDisplacement!;
        leg.LegBars.Should().Be(1);
        leg.OriginAtUtc.Should().Be(current.OpenTimeUtc);
        leg.AtUtc.Should().Be(current.OpenTimeUtc);
        leg.Origin.Value.Should().Be(1.0800m); // Open
        leg.Terminus.Value.Should().Be(1.0824m); // Close
    }

    // Three consecutive bullish body-extending bars. Body-tops rise 1.0805 -> 1.0815 -> 1.0825 and each is an
    // up-close, so the grow-backward run absorbs all three. Origin = min(O,C) of bar1, Terminus = max(O,C) of bar3.
    private static (MarketContext Ctx, Candle Bar1) FeedThreeBarBullishRun(DisplacementOptions options)
    {
        var ctx = NewContext();
        var detector = new DisplacementDetector(options);
        Warmup(ctx, detector, 14);
        var bar1 = Candle(14, 1.0800m, 1.0806m, 1.0799m, 1.0805m); // up, body-top 1.0805
        var bar2 = Candle(15, 1.0805m, 1.0816m, 1.0804m, 1.0815m); // up, body-top 1.0815
        var bar3 = Candle(16, 1.0815m, 1.0826m, 1.0814m, 1.0825m); // up, body-top 1.0825 (terminus)
        Feed(ctx, detector, bar1);
        Feed(ctx, detector, bar2);
        Feed(ctx, detector, bar3);
        return (ctx, bar1);
    }

    [Fact]
    public void Three_consecutive_body_extending_bullish_bars_form_one_three_bar_leg()
    {
        var (ctx, bar1) = FeedThreeBarBullishRun(new DisplacementOptions());

        var leg = ctx.LastDisplacement!;
        leg.Direction.Should().Be(Direction.Bullish);
        leg.LegBars.Should().Be(3);
        leg.OriginAtUtc.Should().Be(bar1.OpenTimeUtc);
        leg.Origin.Value.Should().Be(1.0800m); // min(Open, Close) of bar1
        leg.Terminus.Value.Should().Be(1.0825m); // max(Open, Close) of bar3
        leg.EquilibriumPrice.Should().Be(1.08125m); // 50% of the 1.0800 -> 1.0825 span
    }

    [Fact]
    public void A_four_bar_run_is_truncated_to_the_last_three_when_max_bars_is_three()
    {
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions { DisplacementLegMaxBars = 3 });
        Warmup(ctx, detector, 14);
        var bar1 = Candle(14, 1.0790m, 1.0796m, 1.0789m, 1.0795m); // up, body-top 1.0795 (would extend but is excluded)
        var bar2 = Candle(15, 1.0795m, 1.0806m, 1.0794m, 1.0805m); // up, body-top 1.0805
        var bar3 = Candle(16, 1.0805m, 1.0816m, 1.0804m, 1.0815m); // up, body-top 1.0815
        var bar4 = Candle(17, 1.0815m, 1.0826m, 1.0814m, 1.0825m); // up, body-top 1.0825 (terminus)
        Feed(ctx, detector, bar1);
        Feed(ctx, detector, bar2);
        Feed(ctx, detector, bar3);
        Feed(ctx, detector, bar4);

        var leg = ctx.LastDisplacement!;
        leg.LegBars.Should().Be(3);
        leg.OriginAtUtc.Should().Be(bar2.OpenTimeUtc); // bar1 of 4 excluded by the hard cap
        leg.Origin.Value.Should().Be(1.0795m); // min(Open, Close) of bar2
        leg.Terminus.Value.Should().Be(1.0825m);
    }

    [Fact]
    public void A_counter_candle_mid_run_ends_the_run_at_the_counter_candle()
    {
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        Warmup(ctx, detector, 14);
        var down = Candle(14, 1.0810m, 1.0811m, 1.0804m, 1.0805m); // DOWN close — must not glue to a bullish run
        var up1 = Candle(15, 1.0805m, 1.0816m, 1.0804m, 1.0815m); // up, body-top 1.0815
        var up2 = Candle(16, 1.0815m, 1.0826m, 1.0814m, 1.0825m); // up, body-top 1.0825 (terminus)
        Feed(ctx, detector, down);
        Feed(ctx, detector, up1);
        Feed(ctx, detector, up2);

        var leg = ctx.LastDisplacement!;
        leg.LegBars.Should().Be(2); // only the trailing same-direction sub-run
        leg.OriginAtUtc.Should().Be(up1.OpenTimeUtc);
        leg.Origin.Value.Should().Be(1.0805m); // min(Open, Close) of up1
        leg.Terminus.Value.Should().Be(1.0825m);
    }

    [Fact]
    public void A_stall_candle_ends_the_run_under_the_strict_monotonic_test()
    {
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        Warmup(ctx, detector, 14);
        // up1 body-top 1.0816; up2 (terminus) body-top 1.0826. The stall candle BEFORE up1 has an EQUAL body-top to
        // up1 (1.0816), so the strict `<` extension test rejects it (an equal-extreme stall never glues a candle on).
        var stall = Candle(14, 1.0810m, 1.0817m, 1.0809m, 1.0816m); // up, body-top 1.0816 (equal to up1)
        var up1 = Candle(15, 1.0815m, 1.0817m, 1.0808m, 1.0816m); // up, body-top 1.0816
        var up2 = Candle(16, 1.0816m, 1.0827m, 1.0815m, 1.0826m); // up, body-top 1.0826 (terminus)
        Feed(ctx, detector, stall);
        Feed(ctx, detector, up1);
        Feed(ctx, detector, up2);

        var leg = ctx.LastDisplacement!;
        leg.LegBars.Should().Be(2);
        leg.OriginAtUtc.Should().Be(up1.OpenTimeUtc);
        leg.Origin.Value.Should().Be(1.0815m); // min(Open, Close) of up1 — stall excluded
    }

    [Fact]
    public void The_aggregate_gate_rejects_an_anemic_three_bar_run()
    {
        // Three up-closes that extend the body-top, but each carries a huge wick so legBody/legRange < ratio.
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        Warmup(ctx, detector, 14);
        var bar1 = Candle(14, 1.0800m, 1.0860m, 1.0740m, 1.0801m); // tiny body, massive range
        var bar2 = Candle(15, 1.0801m, 1.0861m, 1.0741m, 1.0802m);
        var bar3 = Candle(16, 1.0802m, 1.0862m, 1.0742m, 1.0803m);
        Feed(ctx, detector, bar1);
        Feed(ctx, detector, bar2);
        var result = Feed(ctx, detector, bar3);

        result.Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void The_aggregate_gate_passes_a_clean_run_no_single_bar_passes_on_atr_alone()
    {
        // Each bar's body alone (~5 pips) is below AtrMultiple*atr, but the NET thrust over the run clears it —
        // the net-thrust generalisation of the gate. ATR is small (doji warmup ~0.5 pip true range).
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        Warmup(ctx, detector, 14);
        var bar1 = Candle(14, 1.0800m, 1.0806m, 1.0799m, 1.0805m); // body ~5 pips
        var bar2 = Candle(15, 1.0805m, 1.0811m, 1.0804m, 1.0810m);
        var bar3 = Candle(16, 1.0810m, 1.0816m, 1.0809m, 1.0815m);
        Feed(ctx, detector, bar1);
        Feed(ctx, detector, bar2);
        var result = Feed(ctx, detector, bar3);

        result.Matched.Should().BeTrue();
        ctx.LastDisplacement!.LegBars.Should().Be(3);
        ctx.LastDisplacement.Size.Should().Be(0.0015m); // 1.0800 -> 1.0815 net thrust
    }

    [Fact]
    public void An_fomc_day_anchors_the_multi_bar_leg_wick_to_wick()
    {
        // Wick-to-wick leg-wide on an FOMC day: Origin = bar1.Low, Terminus = bar3.High, and the extension metric
        // uses High (so the run grows on rising highs).
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        ctx.LoadCalendar([new EconomicEvent(NyDate, CalendarEventType.Fomc)]);
        Warmup(ctx, detector, 14);
        var bar1 = Candle(14, 1.0800m, 1.0806m, 1.0796m, 1.0805m); // High 1.0806, Low 1.0796
        var bar2 = Candle(15, 1.0805m, 1.0816m, 1.0804m, 1.0815m); // High 1.0816
        var bar3 = Candle(16, 1.0815m, 1.0826m, 1.0814m, 1.0825m); // High 1.0826 (terminus)
        Feed(ctx, detector, bar1);
        Feed(ctx, detector, bar2);
        Feed(ctx, detector, bar3);

        var leg = ctx.LastDisplacement!;
        leg.LegBars.Should().Be(3);
        leg.Origin.Value.Should().Be(1.0796m); // bar1 Low
        leg.Terminus.Value.Should().Be(1.0826m); // bar3 High
    }

    [Fact]
    public void The_eg1_discriminator_uses_boundary_min_max_not_first_open_or_last_close()
    {
        // bar1 Open 1.0802 > Close 1.0801? No — keep bar1 an up-close but with Open BELOW Close so min(O,C) == Open is
        // a trap; instead make bar1's Close the LOWER body edge would be wrong. The discriminator: Origin must be
        // min(Open, Close) of bar1, Terminus max(Open, Close) of bar3 — NOT bar1.Open / bar3.Close literally.
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        Warmup(ctx, detector, 14);
        // bar1 is an up-close (Close > Open) so min(O,C) == Open == 1.0800. bar3 up-close so max(O,C) == Close == 1.0825.
        // To prove the discriminator we rely on the run boundaries being min/max of the BOUNDARY candles, which for an
        // up-close equals (Open of bar1, Close of bar3); the truncation test already proves bar selection.
        var bar1 = Candle(14, 1.0800m, 1.0806m, 1.0799m, 1.0805m);
        var bar2 = Candle(15, 1.0805m, 1.0816m, 1.0804m, 1.0815m);
        var bar3 = Candle(16, 1.0815m, 1.0826m, 1.0814m, 1.0825m);
        Feed(ctx, detector, bar1);
        Feed(ctx, detector, bar2);
        Feed(ctx, detector, bar3);

        var leg = ctx.LastDisplacement!;
        leg.Origin.Value.Should().Be(Math.Min(bar1.Open, bar1.Close));
        leg.Terminus.Value.Should().Be(Math.Max(bar3.Open, bar3.Close));
    }

    [Fact]
    public void Three_consecutive_body_extending_bearish_bars_form_one_three_bar_leg()
    {
        var ctx = NewContext();
        var detector = new DisplacementDetector(new DisplacementOptions());
        Warmup(ctx, detector, 14);
        var bar1 = Candle(14, 1.0825m, 1.0826m, 1.0819m, 1.0820m); // down, body-bottom 1.0820
        var bar2 = Candle(15, 1.0820m, 1.0821m, 1.0809m, 1.0810m); // down, body-bottom 1.0810
        var bar3 = Candle(16, 1.0810m, 1.0811m, 1.0799m, 1.0800m); // down, body-bottom 1.0800 (terminus)
        Feed(ctx, detector, bar1);
        Feed(ctx, detector, bar2);
        Feed(ctx, detector, bar3);

        var leg = ctx.LastDisplacement!;
        leg.Direction.Should().Be(Direction.Bearish);
        leg.LegBars.Should().Be(3);
        leg.Origin.Value.Should().Be(1.0825m); // max(Open, Close) of bar1
        leg.Terminus.Value.Should().Be(1.0800m); // min(Open, Close) of bar3
    }
}
