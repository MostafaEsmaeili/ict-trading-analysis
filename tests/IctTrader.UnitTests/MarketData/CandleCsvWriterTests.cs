using FluentAssertions;
using IctTrader.MarketData.Contracts;
using IctTrader.MarketData.Infrastructure.Feeds;

namespace IctTrader.UnitTests.MarketData;

/// <summary>
/// Locks the round-trip contract between <see cref="CandleCsvWriter"/> and <see cref="CsvCandleSource"/> (issue
/// #100): candles written for a backtest must parse back EXACTLY through the Replay feed's CSV reader —
/// symbol/timeframe/UTC time/OHLCV all preserved — so a fetched history drives a deterministic replay.
/// </summary>
public class CandleCsvWriterTests
{
    private static readonly CandleDto[] Candles =
    [
        new("EURUSD", "M5", new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero),
            1.0832m, 1.0840m, 1.0828m, 1.0836m, 123m),
        new("EURUSD", "M5", new DateTimeOffset(2024, 7, 1, 7, 5, 0, TimeSpan.Zero),
            1.0836m, 1.0851m, 1.0835m, 1.0849m, 210m),
    ];

    [Fact]
    public async Task Written_candles_round_trip_through_CsvCandleSource_exactly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ict-csvwriter-{Guid.NewGuid():N}.csv");
        try
        {
            await CandleCsvWriter.WriteAsync(Candles, path, CancellationToken.None);

            var parsed = CsvCandleSource.Load(path);

            parsed.Should().HaveCount(Candles.Length);
            parsed.Should().BeEquivalentTo(Candles, options => options.WithStrictOrdering(),
                "the writer must emit exactly the CsvCandleSource replay format");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_non_zero_utc_offset_open_time_round_trips_as_utc()
    {
        // The writer normalises to UTC, so a candle carrying an offset is stored — and read back — as the same UTC
        // instant (plan §4.8: UTC is the only stored truth).
        var path = Path.Combine(Path.GetTempPath(), $"ict-csvwriter-{Guid.NewGuid():N}.csv");
        var offsetCandle = new CandleDto(
            "GBPUSD", "M15", new DateTimeOffset(2024, 7, 1, 9, 0, 0, TimeSpan.FromHours(2)),
            1.2700m, 1.2720m, 1.2690m, 1.2715m, 88m);
        try
        {
            await CandleCsvWriter.WriteAsync([offsetCandle], path, CancellationToken.None);

            var parsed = CsvCandleSource.Load(path);

            parsed.Should().ContainSingle();
            parsed[0].OpenTimeUtc.Should().Be(new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero));
            parsed[0].OpenTimeUtc.Offset.Should().Be(TimeSpan.Zero);
            parsed[0].Close.Should().Be(1.2715m);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Creates_the_output_directory_when_it_does_not_exist()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ict-csvwriter-dir-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "sub", "EURUSD-M5.csv");
        try
        {
            await CandleCsvWriter.WriteAsync(Candles, path, CancellationToken.None);

            File.Exists(path).Should().BeTrue("the writer must create the directory tree if needed");
            CsvCandleSource.Load(path).Should().HaveCount(Candles.Length);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
