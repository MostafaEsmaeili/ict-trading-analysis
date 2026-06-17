using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the sweep rules (plan §2.5.1 step 4): a wick beyond an untapped pool that CLOSES back inside is a
/// sweep (enabling the opposite-side trade and consuming the swing); a close BEYOND is a run (HRLR), not a
/// sweep. Buy-side and sell-side are mirrored.
/// </summary>
public class LiquiditySweepDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(TimeProvider.System), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Candle(int i, decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, Base.AddMinutes(5 * i), open, high, low, close, 1m);

    private readonly SwingPointDetector _swing = new(new SwingOptions());
    private readonly LiquidityPoolDetector _pool = new(new LiquidityOptions());
    private readonly LiquiditySweepDetector _sweep = new(new LiquidityOptions());

    private DetectorResult Pipeline(MarketContext ctx, Candle candle)
    {
        ctx.Append(candle);
        _swing.Detect(ctx, candle);
        _pool.Detect(ctx, candle);
        return _sweep.Detect(ctx, candle);
    }

    // Forms a swing high at 1.0900 and registers its buy-side pool.
    private void FormSwingHighPool(MarketContext ctx)
    {
        Pipeline(ctx, Candle(0, 1.0880m, 1.0885m, 1.0875m, 1.0882m));
        Pipeline(ctx, Candle(1, 1.0888m, 1.0900m, 1.0885m, 1.0895m)); // pivot high 1.0900
        Pipeline(ctx, Candle(2, 1.0890m, 1.0892m, 1.0878m, 1.0882m));
    }

    [Fact]
    public void Pool_detector_registers_buy_side_liquidity_at_a_swing_high()
    {
        var ctx = NewContext();
        FormSwingHighPool(ctx);

        ctx.LiquidityPools.Should().ContainSingle(p => p.Side == LiquiditySide.BuySide && p.Level.Value == 1.0900m);
    }

    [Fact]
    public void A_wick_above_with_a_close_back_inside_is_a_bearish_sweep()
    {
        var ctx = NewContext();
        FormSwingHighPool(ctx);

        var result = Pipeline(ctx, Candle(3, 1.0884m, 1.0906m, 1.0882m, 1.0895m)); // wick above 1.0900, close below

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bearish);
        result.KeyLevel.Should().Be(1.0900m);
        ctx.LastSweep.Should().NotBeNull();
        ctx.LastSweep!.Value.Direction.Should().Be(Direction.Bearish);
        ctx.SwingPoints.Single(s => s.Kind == SwingKind.High).State.Should().Be(SwingState.Consumed);
    }

    [Fact]
    public void A_close_beyond_the_pool_is_a_run_not_a_sweep()
    {
        var ctx = NewContext();
        FormSwingHighPool(ctx);
        var pool = ctx.LiquidityPools.Single(p => p.Side == LiquiditySide.BuySide);

        var result = Pipeline(ctx, Candle(3, 1.0895m, 1.0908m, 1.0893m, 1.0905m)); // closes ABOVE 1.0900

        result.Should().Be(DetectorResult.NoMatch);
        pool.Consumption.Should().Be(LiquidityConsumption.Run);
    }

    [Fact]
    public void A_wick_below_a_swing_low_with_a_close_back_inside_is_a_bullish_sweep()
    {
        var ctx = NewContext();
        Pipeline(ctx, Candle(0, 1.0860m, 1.0865m, 1.0850m, 1.0858m));
        Pipeline(ctx, Candle(1, 1.0855m, 1.0860m, 1.0840m, 1.0848m)); // pivot low 1.0840
        Pipeline(ctx, Candle(2, 1.0850m, 1.0862m, 1.0846m, 1.0858m));

        var result = Pipeline(ctx, Candle(3, 1.0856m, 1.0860m, 1.0834m, 1.0845m)); // wick below 1.0840, close above

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        result.KeyLevel.Should().Be(1.0840m);
    }
}
