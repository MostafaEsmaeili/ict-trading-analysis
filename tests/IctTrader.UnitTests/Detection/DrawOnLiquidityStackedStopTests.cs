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
/// Locks FVG-SEM-2b §1 — the stacked stop-sizing in <see cref="DrawOnLiquidityDetector"/> (Ep3 L376-413:
/// "survive a stab into the farther" gap). When <see cref="FvgOptions.StrictFirstFvg"/> is ON and the resolved
/// OTE selection carries a stacked farther gap whose far-edge sits BEYOND the swept extreme, the stop widens to
/// clear that farther edge (a symmetric <see cref="DrawOnLiquidityOptions.StopBufferPips"/> beyond it) — never
/// tightens (min/max, not replace). A wider stop lowers RR and can drop below the floor (a faithful NoMatch). The
/// default OFF (StrictFirstFvg=false) keeps the stop byte-identical at the swept extreme regardless of geometry.
/// </summary>
public class DrawOnLiquidityStackedStopTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static readonly Candle Current = new(Eurusd, Timeframe.M5, Base, 1.0830m, 1.0835m, 1.0825m, 1.0830m, 1m);

    private static DrawOnLiquidityDetector StrictDetector() => new(
        new DrawOnLiquidityOptions(), new OteOptions(), new TradeStyleOptions(),
        new FvgOptions { StrictFirstFvg = true }, new SdProjectionOptions());

    private static DrawOnLiquidityDetector LooseDetector() => new(
        new DrawOnLiquidityOptions(), new OteOptions(), new TradeStyleOptions(),
        new FvgOptions(), new SdProjectionOptions());

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    // Bullish §2.5 frame with a STACKED selection: leg 1.0800->1.0900, band [1.0821, 1.0838].
    // Selected (shallowest, StrictFirstFvg) gap mid 1.0830 (bottom 1.0828) == the entry; the farther/deeper gap
    // mid 1.0824 (top 1.0826, bottom 1.0822) is 2 pips from the selection (within the 5-pip proximity) -> stacked,
    // far-edge 1.0822. The swept low is supplied per case so the farther edge sits above OR below it.
    private static MarketContext ArrangeStackedBullish(decimal sweptLow)
    {
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), Base));
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0828m), new Price(1.0832m), Base)); // mid 1.0830 (entry)
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0822m), new Price(1.0826m), Base)); // mid 1.0824 (farther), far-edge 1.0822
        ctx.SetSweep(new SweepRecord(Direction.Bullish, sweptLow, Base, 1));
        ctx.SetMarketStructureShift(new MarketStructureShift(Direction.Bullish, Timeframe.M5, new Price(sweptLow), new Price(1.0850m), Base));
        ctx.SetBias(Direction.Bullish);
        return ctx;
    }

    private static void AddBuySidePool(MarketContext ctx, decimal level)
        => ctx.RegisterLiquidityPool(new LiquidityPool(LiquiditySide.BuySide, new Price(level), 1, Base));

    [Fact]
    public void A_stacked_farther_gap_below_the_swept_low_widens_the_long_stop_beyond_it()
    {
        // §6(1): swept low 1.0827 (just below entry 1.0830); the farther far-edge 1.0822 sits BELOW it.
        // base stop = 1.0827 - 10p = 1.0817; widened = min(1.0817, 1.0822 - 10p = 1.0812) = 1.0812.
        var ctx = ArrangeStackedBullish(sweptLow: 1.0827m);
        AddBuySidePool(ctx, 1.0920m); // reward 90p / risk 18p = 5.0R, clears the floor

        var result = StrictDetector().Detect(ctx, Current);

        result.Matched.Should().BeTrue();
        ((decimal)result.Evidence![EvidenceKeys.StopPrice]).Should().Be(1.0812m); // widened past the farther gap
    }

    [Fact]
    public void A_stacked_farther_gap_above_the_swept_low_leaves_the_long_stop_at_the_swept_extreme()
    {
        // §6(2): swept low 1.0810 (deep); the farther far-edge 1.0822 sits ABOVE the swept extreme.
        // base stop = 1.0810 - 10p = 1.0800; min(1.0800, 1.0822 - 10p = 1.0812) = 1.0800 -> unchanged.
        var ctx = ArrangeStackedBullish(sweptLow: 1.0810m);
        AddBuySidePool(ctx, 1.0920m);

        var result = StrictDetector().Detect(ctx, Current);

        result.Matched.Should().BeTrue();
        ((decimal)result.Evidence![EvidenceKeys.StopPrice]).Should().Be(1.0800m); // the min keeps the swept-extreme stop
    }

    [Fact]
    public void A_stacked_farther_gap_above_the_swept_high_leaves_the_short_stop_at_the_swept_extreme_bearish_mirror()
    {
        // §6(3): bearish mirror. Leg 1.0900->1.0800, band [1.0862, 1.0879]. Selected gap mid 1.0870 (top 1.0872);
        // farther/deeper gap mid 1.0876 (bottom 1.0874, top 1.0878) -> 2 pips, stacked, far-edge (top) 1.0878.
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(Direction.Bearish, Timeframe.M5, new Price(1.0900m), new Price(1.0800m), Base));
        ctx.RegisterFvg(new FairValueGap(Direction.Bearish, Timeframe.M5, new Price(1.0868m), new Price(1.0872m), Base)); // mid 1.0870 (entry)
        ctx.RegisterFvg(new FairValueGap(Direction.Bearish, Timeframe.M5, new Price(1.0874m), new Price(1.0878m), Base)); // mid 1.0876 (farther), far-edge 1.0878
        ctx.SetSweep(new SweepRecord(Direction.Bearish, 1.0873m, Base, 1)); // swept high 1.0873 (just above entry 1.0870)
        ctx.SetMarketStructureShift(new MarketStructureShift(Direction.Bearish, Timeframe.M5, new Price(1.0873m), new Price(1.0850m), Base));
        ctx.SetBias(Direction.Bearish);
        ctx.RegisterLiquidityPool(new LiquidityPool(LiquiditySide.SellSide, new Price(1.0790m), 1, Base)); // 80p draw

        var result = StrictDetector().Detect(ctx, Current);

        // base stop = 1.0873 + 10p = 1.0883; widened = max(1.0883, 1.0878 + 10p = 1.0888) = 1.0888.
        result.Matched.Should().BeTrue();
        ((decimal)result.Evidence![EvidenceKeys.StopPrice]).Should().Be(1.0888m); // widened ABOVE the farther gap
    }

    [Fact]
    public void A_widened_stop_that_drops_reward_to_risk_below_the_floor_no_longer_matches()
    {
        // §6(4): a pool that clears the floor at the swept-extreme stop but FAILS at the widened stop.
        // base stop 1.0817 (risk 13p): 1.0870 draw = 40p -> 3.08R (passes). widened 1.0812 (risk 18p): 40/18 = 2.22R < 2.5.
        var ctx = ArrangeStackedBullish(sweptLow: 1.0827m);
        AddBuySidePool(ctx, 1.0870m);

        StrictDetector().Detect(ctx, Current).Should().Be(DetectorResult.NoMatch);

        // Same pool at the SAME geometry but flag OFF (no widening) keeps the swept-extreme stop -> it matches.
        LooseDetector().Detect(ctx, Current).Matched.Should().BeTrue();
    }

    [Fact]
    public void With_strict_first_fvg_off_the_stacked_geometry_leaves_the_stop_at_the_swept_extreme()
    {
        // §6(5): same stacked context as §6(1) but the flag is OFF -> §1 is skipped, the stop stays at the swept
        // extreme even though a farther gap exists below it. Entry is unchanged (nearest-sweet-spot 1.0830 == shallowest).
        var ctx = ArrangeStackedBullish(sweptLow: 1.0827m);
        AddBuySidePool(ctx, 1.0920m);

        var result = LooseDetector().Detect(ctx, Current);

        result.Matched.Should().BeTrue();
        ((decimal)result.Evidence![EvidenceKeys.EntryPrice]).Should().Be(1.0830m);
        ((decimal)result.Evidence[EvidenceKeys.StopPrice]).Should().Be(1.0817m); // swept 1.0827 - 10p, NOT widened
        result.Evidence.Should().NotContainKey(EvidenceKeys.StackedFartherBound); // no stacking carried when OFF
    }
}
