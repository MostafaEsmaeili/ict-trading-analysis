using System.Globalization;
using IctTrader.MarketData.Contracts;

namespace IctTrader.MarketData.Infrastructure.Feeds;

/// <summary>
/// Loads replay candles from a simple CSV fixture so <see cref="ReplayMarketDataFeed"/> can drive a
/// recorded session (plan §6.1). Format (one header row, then one candle per line, no embedded
/// commas/quotes), parsed with the invariant culture so a fixture means the same thing on any host:
/// <code>Symbol,Timeframe,OpenTimeUtc,Open,High,Low,Close,Volume</code>
/// <c>OpenTimeUtc</c> is read as UTC (the only stored truth — plan §4.8); a value carrying an offset is
/// normalised to UTC. Times/numbers stay culture-invariant; a malformed row fails fast with its line number.
/// </summary>
public static class CsvCandleSource
{
    private const int ExpectedColumns = 8;

    /// <summary>Reads every candle from <paramref name="reader"/> (header row skipped, blank lines ignored).</summary>
    public static IReadOnlyList<CandleDto> Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var candles = new List<CandleDto>();
        var lineNumber = 0;
        var headerSeen = false;

        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!headerSeen)
            {
                headerSeen = true;   // the first non-blank line is the header
                continue;
            }

            candles.Add(ParseRow(line, lineNumber));
        }

        return candles;
    }

    /// <summary>Loads candles from a CSV file at <paramref name="path"/>.</summary>
    public static IReadOnlyList<CandleDto> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new StreamReader(path);
        return Parse(reader);
    }

    private static CandleDto ParseRow(string line, int lineNumber)
    {
        var fields = line.Split(',');
        if (fields.Length != ExpectedColumns)
        {
            throw new FormatException(
                $"CSV line {lineNumber}: expected {ExpectedColumns} columns but found {fields.Length}.");
        }

        try
        {
            return new CandleDto(
                Symbol: fields[0].Trim(),
                Timeframe: fields[1].Trim(),
                OpenTimeUtc: ParseUtc(fields[2]),
                Open: ParseDecimal(fields[3]),
                High: ParseDecimal(fields[4]),
                Low: ParseDecimal(fields[5]),
                Close: ParseDecimal(fields[6]),
                Volume: ParseDecimal(fields[7]));
        }
        catch (FormatException ex)
        {
            throw new FormatException($"CSV line {lineNumber}: {ex.Message}", ex);
        }
    }

    private static DateTimeOffset ParseUtc(string value) => DateTimeOffset.Parse(
        value.Trim(),
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static decimal ParseDecimal(string value) =>
        decimal.Parse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture);
}
