using System.Reflection;
using FluentAssertions;
using IctTrader.MarketData.Contracts;
using IctTrader.MarketData.Infrastructure.Feeds;

namespace IctTrader.UnitTests.MarketData;

/// <summary>
/// Locks the pure OANDA v20 candle parser (issue #94): only <c>complete</c> candles are mapped, the
/// nanosecond RFC3339 time is read as UTC, o/h/l/c strings parse with the invariant culture, the OANDA
/// underscore instrument is normalised to the dashboard form (<c>EUR_USD</c> → <c>EURUSD</c>), and a malformed
/// candle fails fast with a <see cref="FormatException"/>. <see cref="OandaCandleParser"/> is internal, so it is
/// invoked through the InfrastructureType's <c>Parse(string)</c> via reflection to avoid widening its surface.
/// </summary>
public class OandaCandleParserTests
{
    // Two complete candles + one in-progress (complete:false) candle that must be skipped.
    private const string SampleJson = """
    {
      "instrument": "EUR_USD",
      "granularity": "M5",
      "candles": [
        {
          "time": "2024-07-01T07:00:00.000000000Z",
          "mid": { "o": "1.0832", "h": "1.0840", "l": "1.0828", "c": "1.0836" },
          "volume": 123,
          "complete": true
        },
        {
          "time": "2024-07-01T07:05:00.000000000Z",
          "mid": { "o": "1.0836", "h": "1.0851", "l": "1.0835", "c": "1.0849" },
          "volume": 210,
          "complete": true
        },
        {
          "time": "2024-07-01T07:10:00.000000000Z",
          "mid": { "o": "1.0849", "h": "1.0852", "l": "1.0847", "c": "1.0850" },
          "volume": 17,
          "complete": false
        }
      ]
    }
    """;

    private static CandleDto[] Parse(string json)
    {
        var parserType = typeof(OandaMarketDataFeed).Assembly
            .GetType("IctTrader.MarketData.Infrastructure.Feeds.OandaCandleParser", throwOnError: true)!;
        var parse = parserType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)!;

        try
        {
            return (CandleDto[])parse.Invoke(null, [json])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;   // surface the real FormatException to the assertions
        }
    }

    [Fact]
    public void Maps_only_complete_candles_skipping_the_in_progress_one()
    {
        var candles = Parse(SampleJson);

        candles.Should().HaveCount(2, "the third candle is complete:false and must be skipped");
        candles.Select(c => c.OpenTimeUtc).Should().Equal(
            new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 7, 1, 7, 5, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Reads_the_nanosecond_time_as_utc()
    {
        var candle = Parse(SampleJson)[0];

        candle.OpenTimeUtc.Should().Be(new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero));
        candle.OpenTimeUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Parses_ohlc_strings_with_the_invariant_culture()
    {
        var candle = Parse(SampleJson)[0];

        candle.Open.Should().Be(1.0832m);
        candle.High.Should().Be(1.0840m);
        candle.Low.Should().Be(1.0828m);
        candle.Close.Should().Be(1.0836m);
        candle.Volume.Should().Be(123m);
    }

    [Fact]
    public void Normalises_the_instrument_to_the_dashboard_symbol_and_keeps_the_granularity()
    {
        var candle = Parse(SampleJson)[0];

        candle.Symbol.Should().Be("EURUSD", "the OANDA underscore form is normalised for the dashboard");
        candle.Timeframe.Should().Be("M5");
    }

    [Fact]
    public void A_malformed_price_string_fails_fast_with_a_FormatException()
    {
        const string malformed = """
        {
          "instrument": "EUR_USD",
          "granularity": "M5",
          "candles": [
            {
              "time": "2024-07-01T07:00:00.000000000Z",
              "mid": { "o": "not-a-number", "h": "1.0840", "l": "1.0828", "c": "1.0836" },
              "volume": 123,
              "complete": true
            }
          ]
        }
        """;

        var parse = () => Parse(malformed);

        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void A_candle_missing_its_mid_prices_fails_fast_with_a_FormatException()
    {
        const string malformed = """
        {
          "instrument": "EUR_USD",
          "granularity": "M5",
          "candles": [
            { "time": "2024-07-01T07:00:00.000000000Z", "volume": 123, "complete": true }
          ]
        }
        """;

        var parse = () => Parse(malformed);

        parse.Should().Throw<FormatException>();
    }
}
