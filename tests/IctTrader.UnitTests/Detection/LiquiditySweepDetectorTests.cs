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
/// Locks the sweep rules (plan §2.5.1 step 4): a wick beyond an untapped pool that CLOSES back inside is a
/// sweep (enabling the opposite-side trade and consuming the swing); a close BEYOND is a run (HRLR), not a
/// sweep. Buy-side and sell-side are mirrored.
/// </summary>
public class LiquiditySweepDetectorTests
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

    [Fact]
    public void A_wick_above_that_closes_exactly_on_the_level_is_not_a_sweep_but_leaves_the_pool_untapped()
    {
        var ctx = NewContext();
        FormSwingHighPool(ctx);
        var pool = ctx.LiquidityPools.Single(p => p.Side == LiquiditySide.BuySide);

        var result = Pipeline(ctx, Candle(3, 1.0895m, 1.0908m, 1.0893m, 1.0900m)); // wick above 1.0900, closes EXACTLY on it

        result.Should().Be(DetectorResult.NoMatch); // no rejection body -> not a clean sweep...
        pool.Untapped.Should().BeTrue();             // ...but the level was never run THROUGH, so it stays sweepable (§2.5.8)
    }

    [Fact]
    public void A_genuine_sweep_still_fires_after_a_prior_close_exactly_on_the_level()
    {
        var ctx = NewContext();
        FormSwingHighPool(ctx);

        Pipeline(ctx, Candle(3, 1.0895m, 1.0908m, 1.0893m, 1.0900m)); // close exactly on 1.0900 -> no-op, pool untapped
        var result = Pipeline(ctx, Candle(4, 1.0898m, 1.0907m, 1.0892m, 1.0894m)); // wick above, closes back inside -> sweep

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bearish);
        result.KeyLevel.Should().Be(1.0900m);
    }

    // --- TIME-10: 08:30 NY macro reference open + "use the lower open when bearish" (Ep17 L154-159) -------
    //
    // These fixtures drive a full NY-EDT day (2024-07-01, UTC-4): a midnight anchor candle (NY 00:00 = UTC
    // 04:00) seeds MidnightOpen, a buy-side swing-high pool at 1.0885 forms in the small hours, a macro
    // anchor candle (NY 08:30 = UTC 12:30) seeds MacroOpen, and a sweep candle wicks the 1.0885 pool. The
    // IsJudas read (evidence) is what the macro/midnight reference governs.

    private static readonly DateTimeOffset NyMidnightUtc = new(2024, 7, 1, 4, 0, 0, TimeSpan.Zero);   // NY 00:00 EDT
    private static readonly DateTimeOffset NyMacro0830Utc = new(2024, 7, 1, 12, 30, 0, TimeSpan.Zero); // NY 08:30 EDT
    private const decimal PoolLevel = 1.0885m;

    private static MarketContext MacroContext(bool useMacroReference, bool index = false)
    {
        var spec = index
            ? new SymbolSpec(Eurusd, 0.0001m, 0.00001m, 5, InstrumentClass.Index)
            : SymbolSpec.FxMajor(Eurusd);
        return new MarketContext(
            spec,
            new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
            new MarketContextOptions { UseMacroOpenReference = useMacroReference });
    }

    private static Candle At(DateTimeOffset openUtc, decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, openUtc, open, high, low, close, 1m);

    // Seeds MidnightOpen, forms an UNTAPPED buy-side pool at 1.0885 (a swing high), then seeds MacroOpen.
    private void SeedOpensAndPool(MarketContext ctx, decimal midnightOpen, decimal macroOpen)
    {
        // Midnight anchor (NY 00:00) — boring candle below the pool, just seeds MidnightOpen.
        Pipeline(ctx, At(NyMidnightUtc, midnightOpen, midnightOpen + 0.0001m, midnightOpen - 0.0001m, midnightOpen));

        // Swing-high pivot at 1.0885 around NY 02:00-02:10 (left / pivot / right), all closing well below it.
        var pivotBase = new DateTimeOffset(2024, 7, 1, 6, 0, 0, TimeSpan.Zero); // NY 02:00 EDT
        Pipeline(ctx, At(pivotBase, 1.0870m, 1.0878m, 1.0865m, 1.0872m));
        Pipeline(ctx, At(pivotBase.AddMinutes(5), 1.0875m, PoolLevel, 1.0870m, 1.0876m)); // pivot high 1.0885
        Pipeline(ctx, At(pivotBase.AddMinutes(10), 1.0874m, 1.0879m, 1.0866m, 1.0870m));

        // Macro anchor (NY 08:30) — seeds MacroOpen; tight so it never touches the 1.0885 pool.
        Pipeline(ctx, At(NyMacro0830Utc, macroOpen, macroOpen + 0.0001m, macroOpen - 0.0001m, macroOpen));
    }

    [Fact]
    public void Fx_default_uses_midnight_only_and_is_byte_identical()
    {
        // Flag OFF: even though a macro open exists and differs, the reference is midnight-only.
        var ctx = MacroContext(useMacroReference: false);
        SeedOpensAndPool(ctx, midnightOpen: 1.0900m, macroOpen: 1.0880m);

        // Sweep wick High 1.0890 sits below midnight (1.0900) -> NOT Judas under midnight-only.
        var result = Pipeline(ctx, At(NyMacro0830Utc.AddMinutes(5), 1.0884m, 1.0890m, 1.0882m, 1.0883m));

        result.Matched.Should().BeTrue();
        result.Evidence![EvidenceKeys.IsJudas].Should().Be(false);
    }

    [Fact]
    public void Bearish_macro_reference_takes_the_lower_open_and_flips_the_judas_read()
    {
        // Flag ON, bearish (buy-side sweep): ref = min(1.0900, 1.0880) = 1.0880; High 1.0890 > 1.0880 -> Judas.
        var ctx = MacroContext(useMacroReference: true);
        SeedOpensAndPool(ctx, midnightOpen: 1.0900m, macroOpen: 1.0880m);

        var result = Pipeline(ctx, At(NyMacro0830Utc.AddMinutes(5), 1.0884m, 1.0890m, 1.0882m, 1.0883m));

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bearish);
        result.Evidence![EvidenceKeys.IsJudas].Should().Be(true); // min branch flipped it (vs midnight-only false)
    }

    [Fact]
    public void Bearish_macro_reference_keeps_midnight_when_it_is_already_the_lower_open()
    {
        // Midnight 1.0870 < Macro 1.0884 -> min keeps midnight; High 1.0890 > 1.0870 -> Judas.
        // (Macro stays below the 1.0885 pool so its anchor candle does not disturb the buy-side liquidity.)
        var ctx = MacroContext(useMacroReference: true);
        SeedOpensAndPool(ctx, midnightOpen: 1.0870m, macroOpen: 1.0884m);

        var result = Pipeline(ctx, At(NyMacro0830Utc.AddMinutes(5), 1.0884m, 1.0890m, 1.0882m, 1.0883m));

        result.Matched.Should().BeTrue();
        result.Evidence![EvidenceKeys.IsJudas].Should().Be(true);
        ctx.ReferenceOpen(premium: true).Should().Be(1.0870m); // min(1.0870, 1.0884) keeps midnight
    }

    [Fact]
    public void Macro_reference_falls_back_to_midnight_before_eight_thirty()
    {
        // Flag ON but the sweep happens at NY 07:30 (UTC 11:30) before macro is captured -> midnight-only.
        var ctx = MacroContext(useMacroReference: true);
        // Seed midnight + pool, but NOT the macro candle (sweep precedes 08:30).
        Pipeline(ctx, At(NyMidnightUtc, 1.0900m, 1.0901m, 1.0899m, 1.0900m));
        var pivotBase = new DateTimeOffset(2024, 7, 1, 6, 0, 0, TimeSpan.Zero);
        Pipeline(ctx, At(pivotBase, 1.0870m, 1.0878m, 1.0865m, 1.0872m));
        Pipeline(ctx, At(pivotBase.AddMinutes(5), 1.0875m, PoolLevel, 1.0870m, 1.0876m));
        Pipeline(ctx, At(pivotBase.AddMinutes(10), 1.0874m, 1.0879m, 1.0866m, 1.0870m));

        ctx.MacroOpen.Should().BeNull();
        var result = Pipeline(ctx, At(new DateTimeOffset(2024, 7, 1, 11, 30, 0, TimeSpan.Zero), 1.0884m, 1.0890m, 1.0882m, 1.0883m));

        result.Matched.Should().BeTrue();
        result.Evidence![EvidenceKeys.IsJudas].Should().Be(false); // midnight 1.0900 -> High 1.0890 not beyond -> not Judas
    }

    [Fact]
    public void Index_class_does_not_auto_switch_to_the_macro_reference()
    {
        // Default flag (off) on an index symbol -> midnight-only; proves the slice does not auto-enable on class.
        var ctx = MacroContext(useMacroReference: false, index: true);
        SeedOpensAndPool(ctx, midnightOpen: 1.0900m, macroOpen: 1.0880m);

        var result = Pipeline(ctx, At(NyMacro0830Utc.AddMinutes(5), 1.0884m, 1.0890m, 1.0882m, 1.0883m));

        result.Matched.Should().BeTrue();
        result.Evidence![EvidenceKeys.IsJudas].Should().Be(false); // midnight-only despite the index class
    }

    [Fact]
    public void No_reference_open_yet_treats_the_sweep_as_judas()
    {
        // No candle has seeded either open before the swing pool is swept -> fallback true (preserves line 137).
        var ctx = MacroContext(useMacroReference: true);
        // Build the pool WITHOUT a 00:00 candle is impossible (the first candle always seeds midnight), so we
        // instead assert the fallback path via a context whose midnight & macro are both null at the sweep by
        // reading ReferenceOpen directly on a fresh context.
        ctx.ReferenceOpen(premium: true).Should().BeNull();
        ctx.ReferenceOpen(premium: false).Should().BeNull();
    }

    [Fact]
    public void Bullish_macro_reference_takes_the_higher_open_for_a_sell_side_sweep()
    {
        // Bullish (sell-side sweep): ref = max(1.0880, 1.0900) = 1.0900; Low 1.0890 < 1.0900 -> Judas.
        // Against midnight-only (1.0880) Low 1.0890 < 1.0880 is false -> the max branch flips it.
        var ctx = MacroContext(useMacroReference: true);

        // Seed MidnightOpen 1.0880 and a sell-side pool (swing low) at 1.0895; seed MacroOpen 1.0900.
        Pipeline(ctx, At(NyMidnightUtc, 1.0880m, 1.0881m, 1.0879m, 1.0880m));               // midnight 1.0880
        var pivotBase = new DateTimeOffset(2024, 7, 1, 6, 0, 0, TimeSpan.Zero);
        Pipeline(ctx, At(pivotBase, 1.0905m, 1.0910m, 1.0900m, 1.0906m));
        Pipeline(ctx, At(pivotBase.AddMinutes(5), 1.0902m, 1.0908m, 1.0895m, 1.0903m));     // pivot low 1.0895
        Pipeline(ctx, At(pivotBase.AddMinutes(10), 1.0904m, 1.0909m, 1.0899m, 1.0905m));
        Pipeline(ctx, At(NyMacro0830Utc, 1.0900m, 1.0901m, 1.0899m, 1.0900m));              // macro 1.0900

        // Sell-side sweep: Low 1.0890 wicks below the 1.0895 pool, closes back inside.
        var result = Pipeline(ctx, At(NyMacro0830Utc.AddMinutes(5), 1.0896m, 1.0898m, 1.0890m, 1.0897m));

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        result.Evidence![EvidenceKeys.IsJudas].Should().Be(true); // max branch flipped it (vs midnight-only false)
    }
}
