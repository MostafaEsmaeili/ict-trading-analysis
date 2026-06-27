using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Services;

/// <summary>
/// Resamples a finer-timeframe candle series UP to a coarser timeframe (e.g. M1 → M15) so a backtest can run a
/// timeframe we did not fetch natively. Pure and deterministic — no clock, no I/O. Buckets are aligned to the UTC
/// epoch (a 15-minute bucket starts at :00/:15/:30/:45 UTC, an H4 bucket at 00/04/08/… UTC, a D1 bucket at 00:00
/// UTC), and each bucket's OHLCV is the first open, the max high, the min low, the last close, and the summed volume
/// of its members — so the aggregated candle satisfies the <see cref="Candle"/> high/low invariants by construction.
/// <para>The native per-timeframe history (fetched from the broker) is always preferred; this is the fallback for an
/// arbitrary timeframe. A trailing partial bucket (fewer source bars than a full period) is still emitted, since a
/// backtest simply consumes the bars it is given.</para>
/// </summary>
public static class CandleAggregator
{
    /// <summary>
    /// Aggregates <paramref name="source"/> (assumed chronological, one consistent finer timeframe) into
    /// <paramref name="target"/> candles. Throws if <paramref name="target"/> is not strictly coarser than the
    /// source timeframe (resampling only ever goes up — a coarser series cannot synthesise finer bars).
    /// </summary>
    public static IReadOnlyList<Candle> Resample(IReadOnlyList<Candle> source, Timeframe target)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Count == 0)
        {
            return [];
        }

        var sourceTimeframe = source[0].Timeframe;
        if (target == sourceTimeframe)
        {
            return source;
        }

        var targetSpan = target.ToTimeSpan();
        if (targetSpan <= sourceTimeframe.ToTimeSpan())
        {
            throw new ArgumentException(
                $"Cannot resample {sourceTimeframe} up to {target}: the target must be strictly coarser than the source.",
                nameof(target));
        }

        var symbol = source[0].Symbol;
        var targetTicks = targetSpan.Ticks;
        var aggregated = new List<Candle>();

        DateTimeOffset bucketStart = default;
        decimal open = 0m, high = 0m, low = 0m, close = 0m, volume = 0m;
        var hasBucket = false;

        foreach (var candle in source)
        {
            var start = FloorToBucket(candle.OpenTimeUtc, targetTicks);

            if (!hasBucket)
            {
                (bucketStart, open, high, low, close, volume, hasBucket) =
                    (start, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, true);
                continue;
            }

            if (start != bucketStart)
            {
                aggregated.Add(new Candle(symbol, target, bucketStart, open, high, low, close, volume));
                (bucketStart, open, high, low, close, volume) =
                    (start, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume);
                continue;
            }

            high = Math.Max(high, candle.High);
            low = Math.Min(low, candle.Low);
            close = candle.Close;
            volume += candle.Volume;
        }

        if (hasBucket)
        {
            aggregated.Add(new Candle(symbol, target, bucketStart, open, high, low, close, volume));
        }

        return aggregated;
    }

    /// <summary>Floors a UTC instant to the start of its target-duration bucket, aligned to the UTC epoch.</summary>
    private static DateTimeOffset FloorToBucket(DateTimeOffset openTimeUtc, long targetTicks)
    {
        var ticksSinceEpoch = openTimeUtc.UtcTicks;
        var flooredTicks = ticksSinceEpoch / targetTicks * targetTicks;
        return new DateTimeOffset(flooredTicks, TimeSpan.Zero);
    }
}
