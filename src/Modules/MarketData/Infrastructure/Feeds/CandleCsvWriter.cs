using System.Globalization;
using System.Text;
using IctTrader.MarketData.Contracts;

namespace IctTrader.MarketData.Infrastructure.Feeds;

/// <summary>
/// Writes <see cref="CandleDto"/>s to a CSV file in EXACTLY the <see cref="CsvCandleSource"/> replay format (issue
/// #100), so a fetched history round-trips cleanly back through <see cref="CsvCandleSource.Parse"/> and can drive
/// the Replay feed for a backtest:
/// <code>Symbol,Timeframe,OpenTimeUtc,Open,High,Low,Close,Volume</code>
/// One header row, then one candle per line. <c>OpenTimeUtc</c> is written as a round-trippable UTC string (the
/// <c>O</c> format, normalised to UTC so <see cref="CsvCandleSource"/>'s <c>AssumeUniversal|AdjustToUniversal</c>
/// parse reads it back unchanged); o/h/l/c/v are written with the invariant culture so a file means the same on any
/// host (plan §4.8/§6.1). The output directory is created if needed.
/// <para><b>Read-only by shape:</b> this writes ONLY a local CSV file — there is no order/trade/network path here.</para>
/// </summary>
public static class CandleCsvWriter
{
    private const string HeaderRow = "Symbol,Timeframe,OpenTimeUtc,Open,High,Low,Close,Volume";

    /// <summary>The round-trippable date-time format (<c>O</c>) — UTC, parsed back exactly by <see cref="CsvCandleSource"/>.</summary>
    private const string RoundTripTimeFormat = "O";

    /// <summary>Asynchronously writes <paramref name="candles"/> to <paramref name="path"/> (UTF-8, directory created).</summary>
    public static async Task WriteAsync(IEnumerable<CandleDto> candles, string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candles);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        EnsureDirectory(path);

        await using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        await writer.WriteLineAsync(HeaderRow).ConfigureAwait(false);

        foreach (var candle in candles)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(FormatRow(candle)).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronously writes <paramref name="candles"/> to <paramref name="path"/> (UTF-8, directory created).</summary>
    public static void Write(IEnumerable<CandleDto> candles, string path)
    {
        ArgumentNullException.ThrowIfNull(candles);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        EnsureDirectory(path);

        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        writer.WriteLine(HeaderRow);

        foreach (var candle in candles)
        {
            writer.WriteLine(FormatRow(candle));
        }
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Formats one candle as the invariant-culture CSV row <see cref="CsvCandleSource"/> reads back exactly.</summary>
    private static string FormatRow(CandleDto candle) => string.Join(
        ',',
        candle.Symbol,
        candle.Timeframe,
        candle.OpenTimeUtc.ToUniversalTime().ToString(RoundTripTimeFormat, CultureInfo.InvariantCulture),
        candle.Open.ToString(CultureInfo.InvariantCulture),
        candle.High.ToString(CultureInfo.InvariantCulture),
        candle.Low.ToString(CultureInfo.InvariantCulture),
        candle.Close.ToString(CultureInfo.InvariantCulture),
        candle.Volume.ToString(CultureInfo.InvariantCulture));
}
