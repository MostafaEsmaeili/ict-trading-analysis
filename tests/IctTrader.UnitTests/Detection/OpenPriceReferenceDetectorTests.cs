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
/// Locks the OPTIONAL <see cref="ConfluenceCondition.OpenPriceReference"/> emitter (0.50, §2.5.2 step 4 / §2.5.8):
/// at MSS-confirmation the price's relationship to the reference open must AGREE with the locked daily bias —
/// BEARISH wants the confirming price in PREMIUM (above the reference open, the Judas rallied above midnight before
/// reversing), BULLISH wants DISCOUNT (below it). The reference open is the existing
/// <see cref="MarketContext.ReferenceOpen(bool)"/> (midnight / 08:30 macro). The emitted direction is the bias, so the
/// FSM only counts it when it aligns with the MSS lock.
/// </summary>
public class OpenPriceReferenceDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero));

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    // 06:30 UTC = 02:30 NY (London Open) on the EDT day; the first candle of the day sets the midnight open = its Open.
    private static readonly DateTimeOffset London = new(2024, 7, 1, 6, 30, 0, TimeSpan.Zero);

    private static Candle CandleAt(decimal open, decimal close)
    {
        var high = Math.Max(open, close) + 0.0005m;
        var low = Math.Min(open, close) - 0.0005m;
        return new Candle(Eurusd, Timeframe.M5, London, open, high, low, close, 1m);
    }

    private static DetectorResult Detect(Direction? bias, decimal open, decimal close)
    {
        var ctx = NewContext();
        var candle = CandleAt(open, close);
        ctx.Append(candle);           // first candle -> MidnightOpen = open (the reference open, default FX)
        ctx.SetBias(bias);
        return new OpenPriceReferenceDetector(new OpenPriceReferenceOptions()).Detect(ctx, candle);
    }

    [Fact]
    public void Bearish_in_premium_above_the_reference_open_matches_bearish()
    {
        // Reference open = 1.0830; the confirming price 1.0860 sits ABOVE it (premium) -> agrees with the bearish bias.
        var result = Detect(Direction.Bearish, open: 1.0830m, close: 1.0860m);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bearish);
    }

    [Fact]
    public void Bullish_in_discount_below_the_reference_open_matches_bullish()
    {
        // Reference open = 1.0830; the confirming price 1.0805 sits BELOW it (discount) -> agrees with the bullish bias.
        var result = Detect(Direction.Bullish, open: 1.0830m, close: 1.0805m);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
    }

    [Fact]
    public void Bearish_in_discount_below_the_reference_open_does_not_match()
        // A bearish bias but the price is BELOW the reference open (discount, not premium) -> the open-price read
        // disagrees with the bias -> no match.
        => Detect(Direction.Bearish, open: 1.0830m, close: 1.0805m).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void Bullish_in_premium_above_the_reference_open_does_not_match()
        => Detect(Direction.Bullish, open: 1.0830m, close: 1.0860m).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void A_price_exactly_at_the_reference_open_is_neither_premium_nor_discount_so_no_match()
        // Boundary: a close exactly ON the reference open is not strictly premium or discount -> no Judas read.
        => Detect(Direction.Bearish, open: 1.0830m, close: 1.0830m).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void A_neutral_bias_yields_no_match()
        => Detect(bias: null, open: 1.0830m, close: 1.0860m).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void No_reference_open_yet_yields_no_match()
    {
        // A context with no appended candle has no MidnightOpen, so there is no reference open to test against.
        var ctx = NewContext();
        ctx.SetBias(Direction.Bearish);
        var candle = CandleAt(1.0830m, 1.0860m);

        new OpenPriceReferenceDetector(new OpenPriceReferenceOptions()).Detect(ctx, candle)
            .Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void Default_configuration_validates_clean()
        => new OpenPriceReferenceOptions().Validate().Should().BeEmpty();
}
