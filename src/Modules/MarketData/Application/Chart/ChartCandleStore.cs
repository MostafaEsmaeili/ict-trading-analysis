using System.Collections.Concurrent;
using IctTrader.MarketData.Contracts;

namespace IctTrader.MarketData.Application.Chart;

/// <summary>
/// The MarketData module's in-memory chart read-model: a bounded, thread-safe ring buffer of the most-recent
/// <see cref="CandleDto"/> per <c>(Symbol, Timeframe)</c> series (plan §3.0a / §9.1), feeding the dashboard's
/// ICT Pattern Chart. The <see cref="ChartCandleProjectionHandler"/> <see cref="Append"/>s each ingested candle;
/// the <see cref="GetChartCandlesQueryHandler"/> reads a chronological window via <see cref="Recent"/>. Registered
/// as a SINGLETON so the series survive across bus dispatches.
///
/// <para><b>Bounded:</b> each series holds at most <see cref="MaxCandlesPerSeries"/> candles — once full, appending
/// evicts the OLDEST so a long-running feed never grows without bound (a chart window, not an audit log). The cap is
/// a named const, not a magic number.</para>
///
/// <para><b>Keyed by (Symbol, Timeframe):</b> EURUSD M5 and EURUSD M15 are independent series. Matching is
/// case-insensitive (ordinal) so a query for "eurusd"/"m5" finds candles ingested as "EURUSD"/"M5".</para>
///
/// <para><b>Thread-safe:</b> the in-memory bus dispatches sequentially, but each per-series buffer guards with its
/// own lock anyway so it stays correct if a future distributed transport (plan §3.0a) fans handlers out
/// concurrently. Reads copy out a snapshot so a concurrent append can never tear the window the query projects.</para>
///
/// <para><b>Read-only sink (plan §6.3 guardrail):</b> this is an advisory projection of read-only candle events —
/// appending an OHLC bar routes nowhere near an order path.</para>
///
/// <para><b>Deferred:</b> this is in-memory — a Host restart loses the series (it does not warm-start from a
/// persisted candle store). Persistence / warm-start of the chart read-model is a follow-on.</para>
/// </summary>
public sealed class ChartCandleStore
{
    /// <summary>The per-series ring-buffer capacity: the maximum candles retained for one (symbol, timeframe).</summary>
    public const int MaxCandlesPerSeries = 1500;

    // One bounded buffer per (symbol, timeframe) key. ConcurrentDictionary makes get-or-add of a new series
    // thread-safe; each Series guards its own list so concurrent appends to DIFFERENT series never contend.
    private readonly ConcurrentDictionary<SeriesKey, Series> _series = new();

    /// <summary>
    /// Appends one candle to its <c>(Symbol, Timeframe)</c> series, preserving chronological (ingest) order. If the
    /// series is at <see cref="MaxCandlesPerSeries"/>, the OLDEST candle is evicted first so the bound holds.
    /// Thread-safe.
    /// </summary>
    public void Append(CandleDto candle)
    {
        ArgumentNullException.ThrowIfNull(candle);

        var series = _series.GetOrAdd(new SeriesKey(candle.Symbol, candle.Timeframe), static _ => new Series());
        series.Append(candle);
    }

    /// <summary>
    /// Returns up to <paramref name="max"/> of the most-recent candles for <paramref name="symbol"/> /
    /// <paramref name="timeframe"/>, in CHRONOLOGICAL (oldest→newest) order — lightweight-charts requires ascending
    /// time. A non-positive <paramref name="max"/>, or an unknown series, returns an empty list. The returned list is
    /// a snapshot copy — safe to enumerate while appends continue.
    /// </summary>
    public IReadOnlyList<CandleDto> Recent(string symbol, string timeframe, int max)
    {
        if (max <= 0 || !_series.TryGetValue(new SeriesKey(symbol, timeframe), out var series))
        {
            return [];
        }

        return series.Recent(max);
    }

    /// <summary>The composite key; equality is case-insensitive so wire/ingest casing differences match.</summary>
    private readonly record struct SeriesKey(string Symbol, string Timeframe)
    {
        public bool Equals(SeriesKey other) =>
            string.Equals(Symbol, other.Symbol, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Timeframe, other.Timeframe, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() => HashCode.Combine(
            Symbol.GetHashCode(StringComparison.OrdinalIgnoreCase),
            Timeframe.GetHashCode(StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One bounded, lock-guarded FIFO buffer for a single (symbol, timeframe) series.</summary>
    private sealed class Series
    {
        private readonly object _gate = new();

        // A list used as a FIFO ring: append to the tail (newest), evict from the head (oldest) past the cap.
        private readonly List<CandleDto> _candles = new(MaxCandlesPerSeries);

        public void Append(CandleDto candle)
        {
            lock (_gate)
            {
                if (_candles.Count >= MaxCandlesPerSeries)
                {
                    _candles.RemoveAt(0);
                }

                _candles.Add(candle);
            }
        }

        public IReadOnlyList<CandleDto> Recent(int max)
        {
            lock (_gate)
            {
                var take = Math.Min(max, _candles.Count);

                // The newest `take` candles, oldest→newest: copy the tail slice as-is (already chronological).
                var window = new CandleDto[take];
                _candles.CopyTo(_candles.Count - take, window, 0, take);
                return window;
            }
        }
    }
}
