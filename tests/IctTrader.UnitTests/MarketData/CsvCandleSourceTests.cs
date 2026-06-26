using FluentAssertions;
using IctTrader.MarketData.Infrastructure.Feeds;

namespace IctTrader.UnitTests.MarketData;

/// <summary>
/// Locks the CSV replay fixture parser (plan §6.1): header skipped, invariant-culture numbers, UTC time,
/// blank lines ignored, and a malformed row fails fast with its line number.
/// </summary>
public class CsvCandleSourceTests
{
    private const string ValidCsv =
        "Symbol,Timeframe,OpenTimeUtc,Open,High,Low,Close,Volume\n" +
        "EURUSD,M5,2024-01-15T07:00:00Z,1.1000,1.1010,1.0990,1.1005,120\n" +
        "\n" +                                                       // blank line ignored
        "EURUSD,M5,2024-01-15T07:05:00Z,1.1005,1.1020,1.1002,1.1018,98\n";

    [Fact]
    public void Parses_candles_skipping_header_and_blank_lines()
    {
        var candles = CsvCandleSource.Parse(new StringReader(ValidCsv));

        candles.Should().HaveCount(2);
        candles[0].Symbol.Should().Be("EURUSD");
        candles[0].Timeframe.Should().Be("M5");
        candles[0].Open.Should().Be(1.1000m);
        candles[0].Close.Should().Be(1.1005m);
        candles[0].Volume.Should().Be(120m);
        candles[1].High.Should().Be(1.1020m);
    }

    [Fact]
    public void Reads_open_time_as_utc()
    {
        var candles = CsvCandleSource.Parse(new StringReader(ValidCsv));

        candles[0].OpenTimeUtc.Should().Be(new DateTimeOffset(2024, 1, 15, 7, 0, 0, TimeSpan.Zero));
        candles[0].OpenTimeUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Normalises_an_offset_time_to_utc()
    {
        // 07:00 at +02:00 is 05:00 UTC — stored as the single UTC truth (plan §4.8).
        const string offsetCsv =
            "Symbol,Timeframe,OpenTimeUtc,Open,High,Low,Close,Volume\n" +
            "EURUSD,M5,2024-01-15T07:00:00+02:00,1.1,1.1,1.1,1.1,1\n";

        var candles = CsvCandleSource.Parse(new StringReader(offsetCsv));

        candles[0].OpenTimeUtc.Should().Be(new DateTimeOffset(2024, 1, 15, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_row_with_the_wrong_column_count_fails_with_its_line_number()
    {
        const string badCsv =
            "Symbol,Timeframe,OpenTimeUtc,Open,High,Low,Close,Volume\n" +
            "EURUSD,M5,2024-01-15T07:00:00Z,1.1,1.1,1.1\n";   // only 6 columns, on line 2

        var parse = () => CsvCandleSource.Parse(new StringReader(badCsv));

        parse.Should().Throw<FormatException>().WithMessage("*line 2*");
    }

    [Fact]
    public void A_headerless_file_fails_fast_instead_of_dropping_the_first_candle()
    {
        // No header row — the first data line must NOT be silently consumed as a header.
        const string headerless =
            "EURUSD,M5,2024-01-15T07:00:00Z,1.1,1.1,1.1,1.1,1\n" +
            "EURUSD,M5,2024-01-15T07:05:00Z,1.1,1.1,1.1,1.1,1\n";

        var parse = () => CsvCandleSource.Parse(new StringReader(headerless));

        parse.Should().Throw<FormatException>().WithMessage("*expected header*");
    }

    [Fact]
    public void A_wrong_shaped_header_fails_fast()
    {
        const string wrongHeader =
            "Sym,TF,Time,O,H,L,C,V\n" +
            "EURUSD,M5,2024-01-15T07:00:00Z,1.1,1.1,1.1,1.1,1\n";

        var parse = () => CsvCandleSource.Parse(new StringReader(wrongHeader));

        parse.Should().Throw<FormatException>().WithMessage("*expected header*");
    }

    [Fact]
    public void A_row_with_an_unparseable_number_fails_with_its_line_number()
    {
        const string badCsv =
            "Symbol,Timeframe,OpenTimeUtc,Open,High,Low,Close,Volume\n" +
            "EURUSD,M5,2024-01-15T07:00:00Z,not-a-number,1.1,1.1,1.1,1\n";

        var parse = () => CsvCandleSource.Parse(new StringReader(badCsv));

        parse.Should().Throw<FormatException>().WithMessage("*line 2*");
    }
}
