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
/// Locks the daily-bias read (plan §2.5.1 step 1, §2.5.10): the current price in DISCOUNT of the dealing range
/// is a bullish bias, in PREMIUM a bearish bias, exactly at the 50% equilibrium (or with no range) is NEUTRAL —
/// no trade. The §2.5.10 consecutive-close corroboration is off by default and gates the match when enabled.
/// </summary>
public class DailyBiasDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Candle(int i, decimal close, decimal? open = null)
    {
        var o = open ?? close;
        return new(Eurusd, Timeframe.M5, Base.AddMinutes(5 * i), o,
            Math.Max(o, close) + 0.0005m, Math.Min(o, close) - 0.0005m, close, 1m);
    }

    private static DealingRange Range() => new(new Price(1.0800m), new Price(1.0900m), Base); // EQ 1.0850

    private static readonly DailyBiasDetector Detector = new(new DailyBiasOptions());

    [Fact]
    public void Price_in_discount_is_a_bullish_bias()
    {
        var ctx = NewContext();
        ctx.SetDailyRange(Range());
        var candle = Candle(0, 1.0820m); // 20% of range -> discount
        ctx.Append(candle);

        var result = Detector.Detect(ctx, candle);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        ctx.Bias.Should().Be(Direction.Bullish);
    }

    [Fact]
    public void Price_in_premium_is_a_bearish_bias()
    {
        var ctx = NewContext();
        ctx.SetDailyRange(Range());
        var candle = Candle(0, 1.0880m); // 80% -> premium
        ctx.Append(candle);

        var result = Detector.Detect(ctx, candle);

        result.Direction.Should().Be(Direction.Bearish);
        ctx.Bias.Should().Be(Direction.Bearish);
    }

    [Fact]
    public void Price_exactly_at_equilibrium_is_neutral_no_trade()
    {
        var ctx = NewContext();
        ctx.SetDailyRange(Range());
        var candle = Candle(0, 1.0850m); // 50% -> equilibrium

        Detector.Detect(ctx, candle).Should().Be(DetectorResult.NoMatch);
        ctx.Bias.Should().BeNull();
    }

    [Fact]
    public void Without_a_dealing_range_the_bias_is_neutral()
    {
        var ctx = NewContext();
        var candle = Candle(0, 1.0820m);

        Detector.Detect(ctx, candle).Should().Be(DetectorResult.NoMatch);
        ctx.Bias.Should().BeNull();
    }

    [Fact]
    public void Consecutive_close_confirmation_when_enabled_requires_directional_closes()
    {
        var detector = new DailyBiasDetector(
            new DailyBiasOptions { RequireConsecutiveCloseConfirmation = true, ConsecutiveCloseCount = 3 });

        var confirmed = NewContext();
        confirmed.SetDailyRange(Range());
        for (var i = 0; i < 3; i++)
        {
            confirmed.Append(Candle(i, close: 1.0820m, open: 1.0815m)); // up-closes in discount
        }
        detector.Detect(confirmed, Candle(2, close: 1.0820m, open: 1.0815m)).Matched.Should().BeTrue();

        var broken = NewContext();
        broken.SetDailyRange(Range());
        broken.Append(Candle(0, close: 1.0820m, open: 1.0815m));
        broken.Append(Candle(1, close: 1.0820m, open: 1.0815m));
        var down = Candle(2, close: 1.0818m, open: 1.0825m); // a down-close breaks the corroboration
        broken.Append(down);
        detector.Detect(broken, down).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void Default_configuration_validates_clean()
        => new DailyBiasOptions().Validate().Should().BeEmpty();

    // ---- HTF daily-bias gate (RequireReferenceOpenAgreement, §2.5.10 strengthening) ---------------------------

    private static readonly DailyBiasDetector GatedDetector =
        new(new DailyBiasOptions { RequireReferenceOpenAgreement = true });

    // Builds a context with the dealing range set and the first candle appended (open = the midnight/reference open),
    // then detects on that same candle (close = the bias price). Returns (result, ctx) so callers can assert ctx.Bias.
    private static (DetectorResult Result, MarketContext Ctx) DetectGated(decimal midnightOpen, decimal close)
    {
        var ctx = NewContext();
        ctx.SetDailyRange(Range());                          // 1.0800–1.0900, EQ 1.0850
        var candle = Candle(0, close: close, open: midnightOpen);
        ctx.Append(candle);                                  // first candle -> MidnightOpen = its open
        return (GatedDetector.Detect(ctx, candle), ctx);
    }

    [Fact]
    public void Gate_off_by_default_is_byte_identical_even_when_the_open_disagrees()
    {
        // Discount close 1.0820 (bullish) but ABOVE the midnight open 1.0810 — the gate WOULD withhold; with the flag
        // off (default) the match is unchanged. Proves the default path stays byte-identical.
        var ctx = NewContext();
        ctx.SetDailyRange(Range());
        var candle = Candle(0, close: 1.0820m, open: 1.0810m);
        ctx.Append(candle);

        var result = Detector.Detect(ctx, candle); // the DEFAULT detector (gate off)

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
    }

    [Fact]
    public void Gate_on_bullish_below_the_open_agrees_and_matches()
    {
        // Close 1.0820 is in discount (bullish) AND below the midnight open 1.0840 -> agrees -> matches.
        var (result, ctx) = DetectGated(midnightOpen: 1.0840m, close: 1.0820m);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        ctx.Bias.Should().Be(Direction.Bullish);
    }

    [Fact]
    public void Gate_on_bullish_above_the_open_diverges_and_is_withheld_but_bias_is_still_set()
    {
        // Close 1.0820 is in discount (bullish) but ABOVE the midnight open 1.0810 -> the HTF gate withholds the match.
        var (result, ctx) = DetectGated(midnightOpen: 1.0810m, close: 1.0820m);

        result.Should().Be(DetectorResult.NoMatch);
        ctx.Bias.Should().Be(Direction.Bullish); // bias is STILL set, so the MSS lock / PD veto / Judas read are unaffected
    }

    [Fact]
    public void Gate_on_bearish_above_the_open_agrees_and_matches()
    {
        // Close 1.0880 is in premium (bearish) AND above the midnight open 1.0860 -> agrees -> matches.
        var (result, ctx) = DetectGated(midnightOpen: 1.0860m, close: 1.0880m);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bearish);
        ctx.Bias.Should().Be(Direction.Bearish);
    }

    [Fact]
    public void Gate_on_bearish_below_the_open_diverges_and_is_withheld()
        // Close 1.0880 is in premium (bearish) but BELOW the midnight open 1.0890 -> withheld.
        => DetectGated(midnightOpen: 1.0890m, close: 1.0880m).Result.Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void Gate_on_price_exactly_at_the_open_does_not_agree()
        // Strict boundary (mirrors OpenPriceReferenceDetector): a close exactly AT the open is neither above nor below.
        => DetectGated(midnightOpen: 1.0820m, close: 1.0820m).Result.Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void Gate_on_with_no_reference_open_captured_yet_withholds_fail_closed()
    {
        // The range is set and the close is a clean discount (bullish) but NO candle was appended -> no midnight open ->
        // the opt-in gate fails CLOSED (withholds), while the bias is still computed.
        var ctx = NewContext();
        ctx.SetDailyRange(Range());
        var candle = Candle(0, close: 1.0820m, open: 1.0810m); // not appended -> MidnightOpen stays null

        GatedDetector.Detect(ctx, candle).Should().Be(DetectorResult.NoMatch);
        ctx.Bias.Should().Be(Direction.Bullish);
    }

    [Fact]
    public void Per_instrument_override_can_enable_the_gate_for_one_symbol()
    {
        // None (FX) leaves the global default (off) unchanged → byte-identical; a per-instrument override turns it on.
        new DailyBiasOptions().WithInstrumentOverrides(IctTrader.Domain.Instruments.InstrumentOptionOverrides.None)
            .RequireReferenceOpenAgreement.Should().BeFalse();
        new DailyBiasOptions().WithInstrumentOverrides(
                new IctTrader.Domain.Instruments.InstrumentOptionOverrides { RequireReferenceOpenAgreement = true })
            .RequireReferenceOpenAgreement.Should().BeTrue();
    }

    [Fact]
    public void Gate_match_is_consistent_with_the_open_price_reference_confluence()
    {
        // Whenever the gate matches, the existing OpenPriceReference scorer matches the same direction on the same
        // inputs — the gate and the confluence can never contradict (they read the same ReferenceOpen with the same >/<).
        var (gateResult, ctx) = DetectGated(midnightOpen: 1.0840m, close: 1.0820m); // bullish, below open -> both match
        gateResult.Matched.Should().BeTrue();

        var confluence = new OpenPriceReferenceDetector(new OpenPriceReferenceOptions()).Detect(ctx, Candle(0, close: 1.0820m, open: 1.0840m));
        confluence.Matched.Should().BeTrue();
        confluence.Direction.Should().Be(gateResult.Direction);
    }
}
