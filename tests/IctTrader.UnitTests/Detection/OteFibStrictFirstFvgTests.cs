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
/// Locks decision FVG-SEM-2a (Ep3 L376-394): the strict-first-FVG selection (the SHALLOWEST in-band gap on the
/// displacement-leg timeframe — Ep3's "first higher fair value gap", NOT first-formed), the <c>IsSelectedEntry</c>
/// marker (single writer = <see cref="OteFibDetector"/>, clean-then-set, stale-mark teardown on a new leg), and
/// stacked DETECTION (<c>Stacked</c>/<c>StackedFartherBound</c>, carried for FVG-SEM-2b but not yet consumed).
/// The flag defaults OFF (the existing nearest-sweet-spot path stays byte-identical, regression-locked elsewhere).
/// </summary>
public class OteFibStrictFirstFvgTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    // The §2.5.1-step-7 sweet spot anchors the divergence cases: leg 1.0800 -> 1.0900, band [1.0821, 1.0838],
    // sweet spot 70.5% retrace == 1.08295. On a bullish leg the SHALLOWEST gap is the HIGHEST price.
    private const decimal SweetSpotLevel = 1.08295m;

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Candle()
        => new(Eurusd, Timeframe.M5, Base, 1.0850m, 1.0855m, 1.0845m, 1.0850m, 1m);

    private static Displacement BullishLeg() =>
        new(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), Base);

    private static Displacement BearishLeg() =>
        new(Direction.Bearish, Timeframe.M5, new Price(1.0900m), new Price(1.0800m), Base);

    private static FairValueGap BullishFvg(decimal mid, decimal halfSize = 0.0002m, DateTimeOffset? at = null)
        => new(Direction.Bullish, Timeframe.M5, new Price(mid - halfSize), new Price(mid + halfSize), at ?? Base);

    private static FairValueGap BearishFvg(decimal mid, decimal halfSize = 0.0002m, DateTimeOffset? at = null)
        => new(Direction.Bearish, Timeframe.M5, new Price(mid - halfSize), new Price(mid + halfSize), at ?? Base);

    private static OteFibDetector StrictDetector() => new(new OteOptions(), new FvgOptions { StrictFirstFvg = true });

    // ---- Selection ----

    [Fact]
    public void Strict_first_and_sweet_spot_agree_when_the_shallowest_is_also_nearest()
    {
        // §5.2: stacked bullish mids 1.0825 (deep) / 1.0830 (shallow). Shallowest (highest) == nearest the sweet
        // spot (1.08295) here, so strict-first and the default both pick 1.0830 (coincidence lock).
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        ctx.RegisterFvg(BullishFvg(1.0825m));
        ctx.RegisterFvg(BullishFvg(1.0830m));

        StrictDetector().Detect(ctx, Candle()).KeyLevel.Should().Be(1.0830m);
    }

    [Fact]
    public void Strict_first_diverges_from_sweet_spot_by_picking_the_shallowest_bullish_gap()
    {
        // §5.3: the FVG-SEM-2 contract. Deeper 1.0829 sits nearer the sweet spot; shallower 1.0835 is higher (less
        // retrace). Strict-first picks the SHALLOWEST 1.0835; the default sweet-spot path picks 1.0829.
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        ctx.RegisterFvg(BullishFvg(1.0829m));
        ctx.RegisterFvg(BullishFvg(1.0835m));

        StrictDetector().Detect(ctx, Candle()).KeyLevel.Should().Be(1.0835m);
        // The default (flag OFF) keeps the nearest-sweet-spot pick — proves the divergence is real.
        Math.Abs(1.0829m - SweetSpotLevel).Should().BeLessThan(Math.Abs(1.0835m - SweetSpotLevel));
        new OteFibDetector(new OteOptions(), new FvgOptions()).Detect(ctx, Candle()).KeyLevel.Should().Be(1.0829m);
    }

    [Fact]
    public void Strict_first_picks_the_shallowest_bearish_gap_which_is_the_lowest_price()
    {
        // §5.4: bearish mirror of §5.3. Band [1.0862, 1.0879], sweet spot 1.08705. Deeper-toward-origin 1.0871 is
        // nearer the sweet spot; shallower 1.0865 is LOWER (less retrace). Strict-first picks the lowest 1.0865.
        var ctx = NewContext();
        ctx.SetDisplacement(BearishLeg());
        ctx.RegisterFvg(BearishFvg(1.0871m));
        ctx.RegisterFvg(BearishFvg(1.0865m));

        StrictDetector().Detect(ctx, Candle()).KeyLevel.Should().Be(1.0865m);
        new OteFibDetector(new OteOptions(), new FvgOptions()).Detect(ctx, Candle()).KeyLevel.Should().Be(1.0871m);
    }

    [Fact]
    public void A_single_eligible_fvg_is_the_strict_first_and_is_not_stacked()
    {
        // §5.5
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        var only = BullishFvg(1.0830m);
        ctx.RegisterFvg(only);

        StrictDetector().Detect(ctx, Candle()).KeyLevel.Should().Be(1.0830m);
        only.IsSelectedEntry.Should().BeTrue();
        only.Stacked.Should().BeFalse();
    }

    [Fact]
    public void A_depth_tie_breaks_to_the_sweet_spot_then_earliest_formation()
    {
        // §5.6 determinism: two gaps at the SAME depth (same midpoint 1.0830) — the sweet-spot tie-break is also a
        // tie (equal distance), so the earliest FormedAtUtc wins.
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        var earlier = BullishFvg(1.0830m, at: Base);
        var later = BullishFvg(1.0830m, at: Base.AddMinutes(5));
        ctx.RegisterFvg(later);   // registered first, but formed later
        ctx.RegisterFvg(earlier); // formed earliest -> must win the tie

        StrictDetector().Detect(ctx, Candle());

        earlier.IsSelectedEntry.Should().BeTrue();
        later.IsSelectedEntry.Should().BeFalse();
    }

    [Fact]
    public void When_an_order_block_level_is_the_shallowest_no_fvg_is_marked()
    {
        // §5.7: an OB at 1.0837 (shallower/higher than the only FVG at 1.0825) wins the level; because the winner is
        // an OB, NO FVG carries the entry marker (the marker IS the entry FVG).
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        var fvg = BullishFvg(1.0825m);
        ctx.RegisterFvg(fvg);
        ctx.RegisterOrderBlock(new OrderBlock(
            Direction.Bullish, Timeframe.M5,
            new Price(1.0837m), new Price(1.0838m), new Price(1.0836m), new Price(1.0836m), new Price(1.0837m), Base));

        var result = StrictDetector().Detect(ctx, Candle());

        result.KeyLevel.Should().Be(1.0837m);
        fvg.IsSelectedEntry.Should().BeFalse();
    }

    // ---- IsSelectedEntry marker ----

    [Fact]
    public void Exactly_one_open_fvg_is_marked_after_resolution()
    {
        // §5.8
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        ctx.RegisterFvg(BullishFvg(1.0825m));
        ctx.RegisterFvg(BullishFvg(1.0830m));
        ctx.RegisterFvg(BullishFvg(1.0835m));

        StrictDetector().Detect(ctx, Candle());

        ctx.OpenFvgs.Count(f => f.IsSelectedEntry).Should().Be(1);
        ctx.OpenFvgs.Single(f => f.IsSelectedEntry).Midpoint.Should().Be(1.0835m); // shallowest
    }

    [Fact]
    public void A_new_displacement_leg_clears_the_prior_mark_and_the_next_resolve_re_marks()
    {
        // §5.9: a stale mark from the prior leg must not survive into a new leg.
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        var first = BullishFvg(1.0835m);
        ctx.RegisterFvg(first);
        StrictDetector().Detect(ctx, Candle());
        first.IsSelectedEntry.Should().BeTrue();

        // A NEW leg arrives: SetDisplacement must clear the stale IsSelectedEntry/Stacked on every open FVG.
        var second = BullishFvg(1.0830m);
        ctx.RegisterFvg(second);
        ctx.SetDisplacement(BullishLeg());
        first.IsSelectedEntry.Should().BeFalse();

        StrictDetector().Detect(ctx, Candle());
        ctx.OpenFvgs.Single(f => f.IsSelectedEntry).Midpoint.Should().Be(1.0835m); // shallowest of the two
    }

    [Fact]
    public void A_voided_selected_fvg_is_not_re_marked_and_a_different_eligible_gap_is_chosen()
    {
        // §5.10: the selected FVG voids (two-touch) so it leaves the open set; the next Resolve marks a different gap.
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        var shallow = BullishFvg(1.0835m);
        var deeper = BullishFvg(1.0829m);
        ctx.RegisterFvg(shallow);
        ctx.RegisterFvg(deeper);
        StrictDetector().Detect(ctx, Candle());
        shallow.IsSelectedEntry.Should().BeTrue();

        // Void the shallow gap (3 touches voids by default) -> it is no longer open.
        shallow.RegisterTouch(3);
        shallow.RegisterTouch(3);
        shallow.RegisterTouch(3);
        shallow.IsOpen.Should().BeFalse();

        StrictDetector().Detect(ctx, Candle());
        deeper.IsSelectedEntry.Should().BeTrue();
        shallow.IsSelectedEntry.Should().BeFalse();
    }

    // ---- Stacked detection ----

    [Fact]
    public void A_closer_and_a_farther_gap_within_proximity_mark_the_selection_stacked()
    {
        // §5.11: closer 1.0830, farther 1.0824 (near edges 1.0828 and 1.0826 -> 2 pips apart, within 5).
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        var closer = BullishFvg(1.0830m);   // bottom 1.0828, top 1.0832
        var farther = BullishFvg(1.0824m);  // bottom 1.0822, top 1.0826
        ctx.RegisterFvg(closer);
        ctx.RegisterFvg(farther);

        var ote = OteEntryResolver.Resolve(ctx, new OteOptions(), new OteEntryResolver.OteSelectionPolicy(true, new FvgOptions().StackProximityPips));

        ote.Should().NotBeNull();
        closer.Midpoint.Should().Be(1.0830m);
        StrictDetector().Detect(ctx, Candle());
        closer.Stacked.Should().BeTrue();
        ote!.Value.StackedFartherBound.Should().Be(farther.Bottom.Value); // 1.0822, the FAR edge
    }

    [Fact]
    public void Gaps_farther_apart_than_the_proximity_are_not_stacked()
    {
        // §5.12: closer 1.0835 (bottom 1.0833), farther 1.0824 (top 1.0826) -> 7 pips apart, beyond 5.
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        var closer = BullishFvg(1.0835m);
        var farther = BullishFvg(1.0824m);
        ctx.RegisterFvg(closer);
        ctx.RegisterFvg(farther);

        var ote = OteEntryResolver.Resolve(ctx, new OteOptions(), new OteEntryResolver.OteSelectionPolicy(true, new FvgOptions().StackProximityPips));

        StrictDetector().Detect(ctx, Candle());
        closer.Stacked.Should().BeFalse();
        ote!.Value.StackedFartherBound.Should().BeNull();
    }

    [Fact]
    public void A_bearish_stacked_selection_carries_the_farther_gap_top_as_the_bound()
    {
        // §5.13: bearish mirror. Band [1.0862, 1.0879]. closer 1.0865 (top 1.0867), farther 1.0871 (bottom 1.0869)
        // -> 2 pips apart, within 5. The far edge of the farther (deeper, higher) gap is its Top.
        var ctx = NewContext();
        ctx.SetDisplacement(BearishLeg());
        var closer = BearishFvg(1.0865m);   // bottom 1.0863, top 1.0867
        var farther = BearishFvg(1.0871m);  // bottom 1.0869, top 1.0873
        ctx.RegisterFvg(closer);
        ctx.RegisterFvg(farther);

        var ote = OteEntryResolver.Resolve(ctx, new OteOptions(), new OteEntryResolver.OteSelectionPolicy(true, new FvgOptions().StackProximityPips));

        StrictDetector().Detect(ctx, Candle());
        closer.Stacked.Should().BeTrue();
        ote!.Value.StackedFartherBound.Should().Be(farther.Top.Value); // 1.0873, the FAR (upper) edge
    }
}
