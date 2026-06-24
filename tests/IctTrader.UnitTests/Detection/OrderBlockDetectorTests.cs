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
/// Locks the order-block rules (plan §2.5.1 step 6, decision OB-9a): the OB is the CONSECUTIVE
/// opposite-close run before the displacement, anchored at the open of the candle that STARTS the run;
/// its mean-threshold is the BODY midpoint of that anchor candle; its zone High/Low is the cluster span.
/// It is only a confluence with a linked FVG present, and the anchor open must sit in the correct
/// premium/discount half (mirroring the corrected FVG operators).
/// </summary>
public class OrderBlockDetectorTests
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

    private static readonly OrderBlockDetector Detector = new(new OrderBlockOptions());

    // Arranges a bullish displacement preceded by a SINGLE down-close OB candle (open 1.0850), with the
    // displacement equilibrium and (optionally) a linked FVG. Returns the appended displacement candle.
    private static Candle ArrangeBullish(MarketContext ctx, decimal terminus, bool withFvg = true, Timeframe fvgTimeframe = Timeframe.M5)
    {
        ctx.Append(Candle(0, 1.0850m, 1.0852m, 1.0840m, 1.0843m)); // down-close OB candle, open 1.0850
        var current = Candle(1, 1.0844m, 1.0880m, 1.0843m, 1.0878m); // up displacement
        ctx.Append(current);
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0840m), new Price(terminus), current.OpenTimeUtc));
        if (withFvg)
        {
            ctx.RegisterFvg(new FairValueGap(Direction.Bullish, fvgTimeframe, new Price(1.0862m), new Price(1.0865m), Base));
        }

        return current;
    }

    // Appends the given OB candles (oldest first) then a bullish displacement and a linked discount FVG.
    // The terminus places equilibrium so the EARLIEST candle's open sits in discount.
    private static Candle ArrangeBullishRun(MarketContext ctx, decimal terminus, params Candle[] obCandles)
    {
        foreach (var c in obCandles)
        {
            ctx.Append(c);
        }

        var displacementIndex = obCandles.Length;
        var current = Candle(displacementIndex, 1.0844m, 1.0900m, 1.0843m, 1.0898m); // up displacement
        ctx.Append(current);
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0840m), new Price(terminus), current.OpenTimeUtc));
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0862m), new Price(1.0865m), Base));
        return current;
    }

    [Fact]
    public void A_down_close_block_before_an_up_displacement_with_a_linked_fvg_in_discount_is_an_order_block()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, terminus: 1.0880m); // eq 1.0860, OB open 1.0850 in discount

        var result = Detector.Detect(ctx, current);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        result.KeyLevel.Should().Be(1.0850m);
        ctx.OpenOrderBlocks.Should().ContainSingle(ob => ob.Open.Value == 1.0850m);
    }

    [Fact]
    public void A_single_candle_run_zone_is_that_candles_high_and_low()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, terminus: 1.0880m);

        Detector.Detect(ctx, current);

        var ob = ctx.OpenOrderBlocks.Single();
        ob.High.Value.Should().Be(1.0852m); // the single OB candle's high
        ob.Low.Value.Should().Be(1.0840m);  // the single OB candle's low
    }

    [Fact]
    public void A_single_up_close_block_before_a_down_displacement_anchors_at_its_open()
    {
        // Mirror: one up-close candle before a bearish displacement -> KeyLevel == its open.
        var ctx = NewContext();
        ctx.Append(Candle(0, 1.0850m, 1.0860m, 1.0848m, 1.0857m)); // up-close OB candle, open 1.0850
        var current = Candle(1, 1.0856m, 1.0857m, 1.0820m, 1.0822m); // down displacement
        ctx.Append(current);
        ctx.SetDisplacement(new Displacement(Direction.Bearish, Timeframe.M5, new Price(1.0860m), new Price(1.0820m), current.OpenTimeUtc));
        ctx.RegisterFvg(new FairValueGap(Direction.Bearish, Timeframe.M5, new Price(1.0840m), new Price(1.0843m), Base)); // premium gap

        var result = Detector.Detect(ctx, current);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bearish);
        result.KeyLevel.Should().Be(1.0850m);
    }

    [Fact]
    public void A_two_candle_run_anchors_at_the_earliest_open_and_spans_both_for_the_zone()
    {
        var ctx = NewContext();
        // earlier open 1.0860 (high 1.0863, low 1.0856), later open 1.0855 (high 1.0857, low 1.0851)
        var current = ArrangeBullishRun(
            ctx,
            terminus: 1.0900m, // eq 1.0870, both opens in discount
            Candle(0, 1.0860m, 1.0863m, 1.0856m, 1.0857m),  // down-close, run START
            Candle(1, 1.0855m, 1.0857m, 1.0851m, 1.0852m)); // down-close, run END (nearest displacement)

        var result = Detector.Detect(ctx, current);

        result.Matched.Should().BeTrue();
        result.KeyLevel.Should().Be(1.0860m); // the EARLIER / run-start open, NOT 1.0855
        var ob = ctx.OpenOrderBlocks.Single();
        ob.High.Value.Should().Be(1.0863m); // max of the two highs
        ob.Low.Value.Should().Be(1.0851m);  // min of the two lows
    }

    [Fact]
    public void A_three_candle_run_at_the_default_cap_anchors_at_the_earliest()
    {
        var ctx = NewContext();
        var current = ArrangeBullishRun(
            ctx,
            terminus: 1.0905m, // eq 1.08725, all opens in discount
            Candle(0, 1.0865m, 1.0867m, 1.0861m, 1.0862m),  // run START
            Candle(1, 1.0860m, 1.0862m, 1.0856m, 1.0857m),
            Candle(2, 1.0855m, 1.0857m, 1.0851m, 1.0852m)); // run END

        var result = Detector.Detect(ctx, current);

        result.Matched.Should().BeTrue();
        result.KeyLevel.Should().Be(1.0865m); // earliest of three (default cap = 3)
        var ob = ctx.OpenOrderBlocks.Single();
        ob.High.Value.Should().Be(1.0867m); // cluster span high
        ob.Low.Value.Should().Be(1.0851m);  // cluster span low
    }

    [Fact]
    public void A_run_longer_than_the_cap_keeps_only_the_last_n_nearest_the_displacement()
    {
        // Ep9 four-candle cluster, default cap = 3 -> anchor is the 3rd-from-displacement candle (the 2nd
        // chronologically), NOT the 1st of four; zone covers only the retained last 3.
        Candle[] FourRun() =>
        [
            Candle(0, 1.0870m, 1.0872m, 1.0866m, 1.0867m), // 1st of four (run START, would be dropped by cap 3)
            Candle(1, 1.0865m, 1.0867m, 1.0861m, 1.0862m), // 2nd of four (3rd-from-displacement, the capped anchor)
            Candle(2, 1.0860m, 1.0862m, 1.0856m, 1.0857m),
            Candle(3, 1.0855m, 1.0857m, 1.0851m, 1.0852m), // 4th of four (run END, nearest displacement)
        ];

        var cappedCtx = NewContext();
        var cappedCurrent = ArrangeBullishRun(cappedCtx, terminus: 1.0910m, FourRun());
        var capped = Detector.Detect(cappedCtx, cappedCurrent);

        capped.Matched.Should().BeTrue();
        capped.KeyLevel.Should().Be(1.0865m); // the 3rd-from-displacement candle, NOT 1.0870
        cappedCtx.OpenOrderBlocks.Single().High.Value.Should().Be(1.0867m); // retained-3 span high (not 1.0872)

        // With MaxClusterCandles=4 the SAME arrangement admits the whole Ep9 cluster -> anchor = 1st of four.
        var wholeCtx = NewContext();
        var wholeCurrent = ArrangeBullishRun(wholeCtx, terminus: 1.0910m, FourRun());
        var whole = new OrderBlockDetector(new OrderBlockOptions { MaxClusterCandles = 4 }).Detect(wholeCtx, wholeCurrent);

        whole.Matched.Should().BeTrue();
        whole.KeyLevel.Should().Be(1.0870m); // 1st of four
        wholeCtx.OpenOrderBlocks.Single().High.Value.Should().Be(1.0872m); // whole-cluster span high
    }

    [Fact]
    public void A_with_close_or_doji_bar_terminates_the_consecutive_run()
    {
        // An up-close (with-the-move) bar sits between two down-closes before a bullish displacement -> the
        // run is only the consecutive down-closes nearest the displacement; the earlier down-close is excluded.
        var ctx = NewContext();
        var current = ArrangeBullishRun(
            ctx,
            terminus: 1.0905m,
            Candle(0, 1.0858m, 1.0866m, 1.0856m, 1.0860m),  // down-close, but separated from the run by the doji
            Candle(1, 1.0861m, 1.0863m, 1.0859m, 1.0861m),  // DOJI (Close == Open) -> terminates the run
            Candle(2, 1.0860m, 1.0862m, 1.0856m, 1.0857m),  // down-close, run START (first after the doji)
            Candle(3, 1.0855m, 1.0857m, 1.0851m, 1.0852m)); // down-close, run END

        var result = Detector.Detect(ctx, current);

        result.Matched.Should().BeTrue();
        result.KeyLevel.Should().Be(1.0860m); // first down-close AFTER the doji
        var ob = ctx.OpenOrderBlocks.Single();
        ob.High.Value.Should().Be(1.0862m); // span of the two retained down-closes only
        ob.Low.Value.Should().Be(1.0851m);
    }

    [Fact]
    public void The_mean_threshold_is_the_anchor_body_midpoint_not_the_range_midpoint()
    {
        // Anchor Open=1.0850, Close=1.0843, High=1.0852, Low=1.0840.
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, terminus: 1.0880m);

        Detector.Detect(ctx, current);

        var ob = ctx.OpenOrderBlocks.Single();
        var bodyMid = (1.0850m + 1.0843m) / 2m;       // 1.08465
        var rangeMid = 1.0840m + ((1.0852m - 1.0840m) * 0.50m); // 1.0846
        ob.MeanThreshold(0.50m).Should().Be(bodyMid);
        ob.MeanThreshold(0.50m).Should().NotBe(rangeMid); // a regression to range-based fails here
    }

    [Fact]
    public void A_long_wicked_anchor_uses_the_body_midpoint_for_the_mean_threshold()
    {
        // body 1.0843-1.0850 inside a long-wicked range 1.0835-1.0860.
        var ctx = NewContext();
        ctx.Append(Candle(0, 1.0850m, 1.0860m, 1.0835m, 1.0843m)); // long-wicked down-close, open 1.0850
        var current = Candle(1, 1.0844m, 1.0880m, 1.0843m, 1.0878m);
        ctx.Append(current);
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0835m), new Price(1.0880m), current.OpenTimeUtc));
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0862m), new Price(1.0865m), Base));

        Detector.Detect(ctx, current);

        var ob = ctx.OpenOrderBlocks.Single();
        ob.MeanThreshold(0.50m).Should().Be((1.0850m + 1.0843m) / 2m);            // body mid 1.08465
        ob.MeanThreshold(0.50m).Should().NotBe(1.0835m + ((1.0860m - 1.0835m) * 0.50m)); // range mid 1.08475
    }

    [Fact]
    public void A_three_candle_run_zone_uses_the_cluster_extremes_not_the_anchor_alone()
    {
        // The MIDDLE candle has the highest high; the LAST candle the lowest low.
        var ctx = NewContext();
        var current = ArrangeBullishRun(
            ctx,
            terminus: 1.0905m,
            Candle(0, 1.0865m, 1.0867m, 1.0861m, 1.0862m),  // run START / anchor
            Candle(1, 1.0860m, 1.0872m, 1.0856m, 1.0857m),  // highest high 1.0872
            Candle(2, 1.0855m, 1.0857m, 1.0849m, 1.0852m)); // lowest low 1.0849

        Detector.Detect(ctx, current);

        var ob = ctx.OpenOrderBlocks.Single();
        ob.High.Value.Should().Be(1.0872m); // cluster extreme, NOT the anchor's own 1.0867
        ob.Low.Value.Should().Be(1.0849m);  // cluster extreme, NOT the anchor's own 1.0861
    }

    [Fact]
    public void A_consecutive_up_close_cluster_before_a_bearish_displacement_anchors_at_the_earliest_open()
    {
        // Ep3:208 "two candles ... one consecutive bearish order block".
        var ctx = NewContext();
        ctx.Append(Candle(0, 1.0850m, 1.0862m, 1.0848m, 1.0860m)); // up-close, run START, open 1.0850
        ctx.Append(Candle(1, 1.0861m, 1.0867m, 1.0859m, 1.0865m)); // up-close, run END (nearest displacement)
        var current = Candle(2, 1.0866m, 1.0867m, 1.0820m, 1.0822m); // down displacement
        ctx.Append(current);
        ctx.SetDisplacement(new Displacement(Direction.Bearish, Timeframe.M5, new Price(1.0867m), new Price(1.0820m), current.OpenTimeUtc));
        ctx.RegisterFvg(new FairValueGap(Direction.Bearish, Timeframe.M5, new Price(1.0852m), new Price(1.0855m), Base)); // premium gap

        var result = Detector.Detect(ctx, current);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bearish);
        result.KeyLevel.Should().Be(1.0850m); // earliest up-close open
        var ob = ctx.OpenOrderBlocks.Single();
        ob.High.Value.Should().Be(1.0867m); // cluster span
        ob.Low.Value.Should().Be(1.0848m);
    }

    [Fact]
    public void An_order_block_without_a_linked_fvg_is_rejected()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, terminus: 1.0880m, withFvg: false);

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void An_order_block_whose_run_start_open_is_in_premium_is_rejected()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, terminus: 1.0858m); // eq 1.0849, OB open 1.0850 in premium

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void The_correct_half_gate_keys_on_the_run_start_open_not_the_last_candle()
    {
        // Two-candle run: run-START open 1.0860 sits in premium, the LATER open 1.0855 sits in discount.
        // The gate must key on the run START (1.0860) -> NoMatch.
        var ctx = NewContext();
        var current = ArrangeBullishRun(
            ctx,
            terminus: 1.0856m, // eq ~1.0848 -> 1.0860 is premium, 1.0855 is premium too; tune below
            Candle(0, 1.0860m, 1.0863m, 1.0856m, 1.0857m),
            Candle(1, 1.0855m, 1.0857m, 1.0851m, 1.0852m));

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void No_opposite_close_candle_means_no_order_block()
    {
        var ctx = NewContext();
        ctx.Append(Candle(0, 1.0840m, 1.0852m, 1.0839m, 1.0850m)); // up-close, not a bullish OB candle
        var current = Candle(1, 1.0851m, 1.0880m, 1.0850m, 1.0878m);
        ctx.Append(current);
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0840m), new Price(1.0880m), current.OpenTimeUtc));
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0862m), new Price(1.0865m), Base));

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void No_candle_precedes_the_displacement_means_no_order_block()
    {
        // The displacement candle is the only / first bar in the window -> end < 0 -> NoMatch.
        var ctx = NewContext();
        var current = Candle(0, 1.0844m, 1.0880m, 1.0843m, 1.0878m); // displacement is the FIRST bar
        ctx.Append(current);
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0840m), new Price(1.0880m), current.OpenTimeUtc));
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0862m), new Price(1.0865m), Base));

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_with_close_bar_adjacent_to_the_displacement_is_not_searched_past()
    {
        // The bar immediately preceding a bullish displacement is an UP-close (closed WITH the move). We do
        // NOT search backwards past it to find an older down-close run -> NoMatch.
        var ctx = NewContext();
        ctx.Append(Candle(0, 1.0860m, 1.0862m, 1.0856m, 1.0857m)); // down-close (an older opposite-close run)
        ctx.Append(Candle(1, 1.0856m, 1.0866m, 1.0855m, 1.0864m)); // UP-close adjacent to displacement
        var current = Candle(2, 1.0865m, 1.0900m, 1.0864m, 1.0898m);
        ctx.Append(current);
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0840m), new Price(1.0900m), current.OpenTimeUtc));
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0862m), new Price(1.0865m), Base));

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_cross_timeframe_fvg_does_not_link_by_default_but_links_when_same_timeframe_is_not_required()
    {
        // Default RequireSameTimeframeFvg=true rejects a finer-TF gap (§2.5.7-deferred approximation of true
        // leg membership); turning the flag off restores the §2.5 step-6 15m->1m finer-TF link.
        var strictCtx = NewContext();
        var strictCurrent = ArrangeBullish(strictCtx, terminus: 1.0880m, fvgTimeframe: Timeframe.M1); // OB on M5, FVG on M1
        new OrderBlockDetector(new OrderBlockOptions())
            .Detect(strictCtx, strictCurrent).Should().Be(DetectorResult.NoMatch);

        var looseCtx = NewContext();
        var looseCurrent = ArrangeBullish(looseCtx, terminus: 1.0880m, fvgTimeframe: Timeframe.M1);
        new OrderBlockDetector(new OrderBlockOptions { RequireSameTimeframeFvg = false })
            .Detect(looseCtx, looseCurrent).Matched.Should().BeTrue();
    }
}
