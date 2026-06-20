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
/// Locks the draw-on-liquidity reward-to-risk gate (plan §2.5.1 steps 2/8/9): with a confirmed bias-aligned
/// shift, the entry at the OTE array level, and the stop beyond the swept extreme, the §2.5.2 DrawTargetRrMet
/// condition matches only when an UNTAPPED opposite-side pool beyond the entry gives at least the style RR floor.
/// You sweep one side and draw to the other; a counter-bias direction, an invalidated shift, or no sufficient
/// draw all yield no match.
/// </summary>
public class DrawOnLiquidityDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static readonly DrawOnLiquidityDetector Detector =
        new(new DrawOnLiquidityOptions(), new OteOptions(), new TradeStyleOptions());

    private static readonly Candle Current = new(Eurusd, Timeframe.M5, Base, 1.0830m, 1.0835m, 1.0825m, 1.0830m, 1m);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    // Bullish §2.5 frame: leg 1.0800->1.0900, an FVG entry at 1.0832, a swept low 1.0810 (stop 1.0800,
    // risk 32 pips). Pools are added per test.
    private static MarketContext ArrangeBullishFrame(Direction? bias = Direction.Bullish)
    {
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), Base));
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0828m), new Price(1.0836m), Base)); // mid 1.0832
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0810m, Base, 1));
        ctx.SetMarketStructureShift(new MarketStructureShift(Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0850m), Base));
        if (bias is { } b)
        {
            ctx.SetBias(b);
        }

        return ctx;
    }

    private static void AddBuySidePool(MarketContext ctx, decimal level, int strength = 1)
        => ctx.RegisterLiquidityPool(new LiquidityPool(LiquiditySide.BuySide, new Price(level), strength, Base));

    [Fact]
    public void A_sufficient_opposing_draw_meets_the_required_reward_to_risk()
    {
        var ctx = ArrangeBullishFrame();
        AddBuySidePool(ctx, 1.0920m); // reward 88 pips / risk 32 pips = 2.75R

        var result = Detector.Detect(ctx, Current);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        result.KeyLevel.Should().Be(1.0920m);
        ((decimal)result.Evidence![EvidenceKeys.RewardRatio]).Should().BeGreaterThanOrEqualTo(2.5m);
        result.Evidence.Should().ContainKeys(EvidenceKeys.EntryPrice, EvidenceKeys.StopPrice, EvidenceKeys.TargetPrice);
    }

    [Fact]
    public void A_draw_too_close_to_clear_the_floor_does_not_match()
    {
        var ctx = ArrangeBullishFrame();
        AddBuySidePool(ctx, 1.0850m); // reward 18 pips / risk 32 pips = 0.56R

        Detector.Detect(ctx, Current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void No_opposing_draw_means_no_match()
        => Detector.Detect(ArrangeBullishFrame(), Current).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void A_counter_bias_direction_is_rejected()
    {
        var ctx = ArrangeBullishFrame(bias: Direction.Bearish); // shift bullish but daily bias bearish
        AddBuySidePool(ctx, 1.0920m);

        Detector.Detect(ctx, Current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void An_invalidated_shift_has_no_executable_direction()
    {
        var ctx = ArrangeBullishFrame();
        AddBuySidePool(ctx, 1.0920m);
        ctx.LastMss!.Invalidate();

        Detector.Detect(ctx, Current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void The_nearest_qualifying_draw_is_chosen()
    {
        var ctx = ArrangeBullishFrame();
        AddBuySidePool(ctx, 1.0960m); // 4.0R, farther
        AddBuySidePool(ctx, 1.0920m); // 2.75R, nearest qualifying

        Detector.Detect(ctx, Current).KeyLevel.Should().Be(1.0920m);
    }

    [Fact]
    public void A_bearish_setup_draws_to_sell_side_liquidity_below()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(Direction.Bearish, Timeframe.M5, new Price(1.0900m), new Price(1.0800m), Base));
        ctx.RegisterFvg(new FairValueGap(Direction.Bearish, Timeframe.M5, new Price(1.0866m), new Price(1.0874m), Base)); // mid 1.0870
        ctx.SetSweep(new SweepRecord(Direction.Bearish, 1.0890m, Base, 1));
        ctx.SetMarketStructureShift(new MarketStructureShift(Direction.Bearish, Timeframe.M5, new Price(1.0890m), new Price(1.0850m), Base));
        ctx.SetBias(Direction.Bearish);
        ctx.RegisterLiquidityPool(new LiquidityPool(LiquiditySide.SellSide, new Price(1.0790m), 1, Base)); // 80 pips / 30 = 2.67R

        var result = Detector.Detect(ctx, Current);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bearish);
        result.KeyLevel.Should().Be(1.0790m);
    }

    [Fact]
    public void Default_configuration_validates_clean()
        => new DrawOnLiquidityOptions().Validate().Should().BeEmpty();
}
