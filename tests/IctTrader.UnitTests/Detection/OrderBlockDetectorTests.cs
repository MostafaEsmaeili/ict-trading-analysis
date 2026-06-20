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
/// Locks the order-block rules (plan §2.5.1 step 6): the last opposite-close candle before the displacement
/// is the OB, it is only a confluence with a linked FVG present, and its open must sit in the correct
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

    // Arranges a bullish displacement preceded by a down-close OB candle (open 1.0850), with the displacement
    // equilibrium and (optionally) a linked FVG. Returns the appended displacement candle.
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
    public void An_order_block_without_a_linked_fvg_is_rejected()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, terminus: 1.0880m, withFvg: false);

        Detector.Detect(ctx, current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void An_order_block_whose_open_is_in_premium_is_rejected()
    {
        var ctx = NewContext();
        var current = ArrangeBullish(ctx, terminus: 1.0858m); // eq 1.0849, OB open 1.0850 in premium

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
