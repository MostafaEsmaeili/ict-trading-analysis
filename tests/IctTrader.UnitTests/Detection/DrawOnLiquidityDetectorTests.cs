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
        new(new DrawOnLiquidityOptions(), new OteOptions(), new TradeStyleOptions(), new FvgOptions(), new SdProjectionOptions());

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
        result.Evidence.Should().NotContainKey(EvidenceKeys.SdTargetPrices); // SD disabled by default
    }

    [Fact]
    public void With_sd_projection_enabled_the_match_carries_the_sd_tier_prices()
    {
        // TGR-1/2: SD targets ride the draw evidence (additive). Leg 1.0800->1.0900 -> -1/-1.5/-2 SD = terminus + n*0.0100.
        var ctx = ArrangeBullishFrame();
        AddBuySidePool(ctx, 1.0920m);
        var detector = new DrawOnLiquidityDetector(
            new DrawOnLiquidityOptions(), new OteOptions(), new TradeStyleOptions(), new FvgOptions(),
            new SdProjectionOptions { Enabled = true });

        var result = detector.Detect(ctx, Current);

        result.Matched.Should().BeTrue();
        result.Evidence![EvidenceKeys.RewardRatio].Should().Be(2.75m); // SD does NOT change the gated RR
        ((decimal[])result.Evidence[EvidenceKeys.SdTargetPrices]).Should().Equal(1.1000m, 1.1050m, 1.1100m);
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
    public void Consequent_encroachment_prices_entry_stop_and_rr_from_the_fvg_midpoint()
    {
        // FillZone = ConsequentEncroachment: the entry is the selected FVG's CE (== its midpoint here, 1.0832); the
        // stop is UNCHANGED (beyond the swept extreme 1.0810 - 10 pip buffer = 1.0800); RR recomputes entry->draw.
        var ctx = ArrangeBullishFrame();
        AddBuySidePool(ctx, 1.0920m);
        var detector = new DrawOnLiquidityDetector(
            new DrawOnLiquidityOptions(),
            new OteOptions { FillZone = EntryFillZone.ConsequentEncroachment },
            new TradeStyleOptions(),
            new FvgOptions(),
            new SdProjectionOptions());

        var result = detector.Detect(ctx, Current);

        result.Matched.Should().BeTrue();
        var entry = (decimal)result.Evidence![EvidenceKeys.EntryPrice];
        var stop = (decimal)result.Evidence[EvidenceKeys.StopPrice];
        var target = (decimal)result.Evidence[EvidenceKeys.TargetPrice];
        entry.Should().Be(1.0832m);  // FVG [1.0828, 1.0836] CE = 1.0832
        stop.Should().Be(1.0800m);   // unchanged: swept low 1.0810 - 10 pip buffer
        var expectedRr = Math.Abs(target - entry) / Math.Abs(entry - stop);
        ((decimal)result.Evidence[EvidenceKeys.RewardRatio]).Should().Be(expectedRr);
    }

    [Fact]
    public void A_setup_below_the_rr_floor_at_the_consequent_encroachment_entry_is_no_match()
    {
        // The RR-floor gate uses the CE geometry consistently: with a SHALLOWER CE entry the entry sits closer to
        // the draw, so a draw that no longer clears the style floor at CE is faithfully a NoMatch (not a bug).
        // Leg 1.0800->1.0900, swept low 1.0810, stop 1.0800 (risk vs CE = 0.0032). A draw at 1.0860 gives only
        // 0.0028/0.0032 = 0.875R < the 2:1 floor.
        var ctx = ArrangeBullishFrame();
        AddBuySidePool(ctx, 1.0860m);
        var detector = new DrawOnLiquidityDetector(
            new DrawOnLiquidityOptions(),
            new OteOptions { FillZone = EntryFillZone.ConsequentEncroachment },
            new TradeStyleOptions(),
            new FvgOptions(),
            new SdProjectionOptions());

        detector.Detect(ctx, Current).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void Default_configuration_validates_clean()
        => new DrawOnLiquidityOptions().Validate().Should().BeEmpty();
}
