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

    private static Candle CandleAt(int i, decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, Base.AddMinutes(5 * i), open, high, low, close, 1m);

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
            // The sweep must STRICTLY precede the breaking member (TIME-11-12) — one bar before the current bar.
            ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0850m, Base, ctx.BarsProcessed - 1));
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

    [Fact]
    public void An_mss_still_fires_when_the_swing_detector_already_breached_the_swing_this_candle()
    {
        // The breach-vs-MSS ordering race (spec §5 item 19): when SwingPointDetector runs first it breaches
        // the very swing the displacement closes through. The MSS must still claim a swing breached by THIS
        // same candle, so the pipeline order cannot drop a legitimate shift.
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, closeAboveSwing: 1.0920m);

        new SwingPointDetector(new SwingOptions()).Detect(ctx, current);
        ctx.SwingPoints.Single().State.Should().Be(SwingState.Breached); // pre-breached by the swing detector

        var result = Detector.Detect(ctx, current);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        ctx.LastMss!.IsConfirmed.Should().BeTrue();
    }

    [Fact]
    public void A_swing_breached_on_an_earlier_bar_stays_stale_structure()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, closeAboveSwing: 1.0920m);
        ctx.SwingPoints.Single().Breach(Base.AddMinutes(-5)); // breached by a PRIOR candle, not this one

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    // ---- TIME-11-12: MSS on any candle of a multi-candle displacement leg ----

    // Arranges a 3-bar bullish leg over a swing high at 1.0900 where bar2 is the FIRST member to close above the
    // swing. Returns the terminus candle (bar3). A precedent same-direction sweep is placed before the run.
    private static Candle ArrangeThreeBarLegBreakingOnBar2(
        MarketContext ctx, decimal bar2Close = 1.0915m, decimal bar1Close = 1.0895m, long sweepBarIndex = 0)
    {
        ctx.RegisterSwingPoint(new SwingPoint(SwingKind.High, Timeframe.M5, new Price(1.0900m), Base));
        var bar1 = CandleAt(1, 1.0890m, bar1Close + 0.0005m, 1.0889m, bar1Close); // up, below the swing
        var bar2 = CandleAt(2, bar1Close, bar2Close + 0.0005m, bar1Close - 0.0001m, bar2Close); // up, FIRST above swing
        var bar3 = CandleAt(3, bar2Close, 1.0930m, bar2Close - 0.0001m, 1.0925m); // up, terminus, also above swing
        ctx.Append(bar1);
        ctx.Append(bar2);
        ctx.Append(bar3);
        ctx.SetDisplacement(new Displacement(
            Direction.Bullish, Timeframe.M5, new Price(1.0890m), new Price(1.0925m), bar3.OpenTimeUtc, bar1.OpenTimeUtc, 3));
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0850m, Base, sweepBarIndex));
        return bar3;
    }

    [Fact]
    public void An_mss_fires_on_the_interior_member_that_first_breaks_the_swing()
    {
        var ctx = NewContext();
        var bar3 = ArrangeThreeBarLegBreakingOnBar2(ctx);

        var result = Detector.Detect(ctx, bar3);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        ctx.LastMss!.IsConfirmed.Should().BeTrue();
        ctx.LastMss.AtUtc.Should().Be(Base.AddMinutes(5 * 2)); // bar2, the earliest break — not the terminus bar3
        ctx.LastMss.CloseLevel.Value.Should().Be(1.0915m); // bar2.Close
        var swing = ctx.SwingPoints.Single();
        swing.State.Should().Be(SwingState.Breached);
        swing.WasBreachedOn(Base.AddMinutes(5 * 2)).Should().BeTrue(); // Breach stamped on bar2
    }

    [Fact]
    public void A_wick_only_break_across_the_leg_is_no_mss()
    {
        // A member High pierces 1.0900 but NO member closes beyond it.
        var ctx = NewContext();
        ctx.RegisterSwingPoint(new SwingPoint(SwingKind.High, Timeframe.M5, new Price(1.0900m), Base));
        var bar1 = CandleAt(1, 1.0890m, 1.0905m, 1.0889m, 1.0895m); // wick above 1.0900, closes below
        var bar2 = CandleAt(2, 1.0895m, 1.0906m, 1.0894m, 1.0898m); // wick above, closes below
        var bar3 = CandleAt(3, 1.0898m, 1.0904m, 1.0897m, 1.0899m); // wick above, closes below (terminus)
        ctx.Append(bar1);
        ctx.Append(bar2);
        ctx.Append(bar3);
        ctx.SetDisplacement(new Displacement(
            Direction.Bullish, Timeframe.M5, new Price(1.0890m), new Price(1.0899m), bar3.OpenTimeUtc, bar1.OpenTimeUtc, 3));
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0850m, Base, 0));

        Detector.Detect(ctx, bar3).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_weak_break_below_the_minimum_across_the_leg_is_no_mss()
    {
        // Every member that closes beyond the swing does so by only 0.5 pip — below CloseBeyondMinPips (1.0).
        var ctx = NewContext();
        ctx.RegisterSwingPoint(new SwingPoint(SwingKind.High, Timeframe.M5, new Price(1.0900m), Base));
        var bar1 = CandleAt(1, 1.0890m, 1.0896m, 1.0889m, 1.0895m); // below the swing
        var bar2 = CandleAt(2, 1.0895m, 1.09015m, 1.0894m, 1.09005m); // 0.5 pip beyond
        var bar3 = CandleAt(3, 1.09005m, 1.09015m, 1.09000m, 1.09005m); // 0.5 pip beyond (terminus)
        ctx.Append(bar1);
        ctx.Append(bar2);
        ctx.Append(bar3);
        ctx.SetDisplacement(new Displacement(
            Direction.Bullish, Timeframe.M5, new Price(1.0890m), new Price(1.09005m), bar3.OpenTimeUtc, bar1.OpenTimeUtc, 3));
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0850m, Base, 0));

        Detector.Detect(ctx, bar3).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_sweep_on_the_breaking_members_own_bar_does_not_satisfy_the_strict_precede()
    {
        var ctx = NewContext();
        // Breaking member is bar2; its bar index is BarsProcessed(3) - (lastIdx 2 - memberIdx 1) = 2.
        // A sweep AT bar index 2 is not strictly before the break.
        var bar3 = ArrangeThreeBarLegBreakingOnBar2(ctx, sweepBarIndex: 2);

        Detector.Detect(ctx, bar3).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_sweep_one_bar_before_the_breaking_member_confirms()
    {
        var ctx = NewContext();
        var bar3 = ArrangeThreeBarLegBreakingOnBar2(ctx, sweepBarIndex: 1); // strictly before the breaking bar index 2

        Detector.Detect(ctx, bar3).Matched.Should().BeTrue();
    }

    [Fact]
    public void A_sweep_outside_the_window_to_the_interior_breaking_member_is_no_mss()
    {
        var ctx = NewContext();
        // breakingMemberBarIndex == 2; SweepToMssMaxBars == 5, so a sweep at index -4 is 6 bars away -> rejected.
        var bar3 = ArrangeThreeBarLegBreakingOnBar2(ctx, sweepBarIndex: -4);

        Detector.Detect(ctx, bar3).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_swing_forming_after_an_earlier_member_is_not_retro_broken_by_it()
    {
        // bar2 closes above 1.0900, but the swing formed AFTER bar2 (its FormedAtUtc is later) — the causality guard
        // forbids bar2 from breaking it. Only bar3 (terminus, formed after the swing) may break it.
        var ctx = NewContext();
        var bar1 = CandleAt(1, 1.0890m, 1.0896m, 1.0889m, 1.0895m);
        var bar2 = CandleAt(2, 1.0895m, 1.0916m, 1.0894m, 1.0915m); // closes above 1.0900
        var bar3 = CandleAt(3, 1.0915m, 1.0930m, 1.0914m, 1.0925m); // terminus, closes above 1.0900
        // Swing forms at bar3's time — AFTER bar2 — so bar2 cannot have broken it.
        ctx.RegisterSwingPoint(new SwingPoint(SwingKind.High, Timeframe.M5, new Price(1.0900m), bar3.OpenTimeUtc));
        ctx.Append(bar1);
        ctx.Append(bar2);
        ctx.Append(bar3);
        ctx.SetDisplacement(new Displacement(
            Direction.Bullish, Timeframe.M5, new Price(1.0890m), new Price(1.0925m), bar3.OpenTimeUtc, bar1.OpenTimeUtc, 3));
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0850m, Base, 0));

        var result = Detector.Detect(ctx, bar3);

        result.Matched.Should().BeTrue();
        ctx.LastMss!.AtUtc.Should().Be(bar3.OpenTimeUtc); // bar2 skipped by the causality guard; bar3 confirms
    }

    [Fact]
    public void A_window_pruned_below_the_leg_span_is_a_fail_safe_no_mss()
    {
        // The leg claims 3 bars but only the terminus survives in the window -> members.Count (1) != LegBars (3).
        var ctx = NewContext();
        ctx.RegisterSwingPoint(new SwingPoint(SwingKind.High, Timeframe.M5, new Price(1.0900m), Base));
        var bar3 = CandleAt(3, 1.0915m, 1.0930m, 1.0914m, 1.0925m);
        ctx.Append(bar3); // only the terminus appended
        ctx.SetDisplacement(new Displacement(
            Direction.Bullish, Timeframe.M5, new Price(1.0890m), new Price(1.0925m), bar3.OpenTimeUtc, Base.AddMinutes(5), 3));
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0850m, Base, 0));

        Detector.Detect(ctx, bar3).Should().Be(DetectorResult.NoMatch);
    }
}
