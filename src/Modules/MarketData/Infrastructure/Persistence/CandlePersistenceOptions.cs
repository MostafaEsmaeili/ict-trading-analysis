namespace IctTrader.MarketData.Infrastructure.Persistence;

/// <summary>
/// Configuration for the candle persistence background writer (plan §7 — no magic numbers).
/// Bound from <c>Ict:MarketData:Persistence</c>; defaults keep the batched dual-write transparent to the
/// scan path — a DB outage never blocks the in-memory <see cref="Chart.ChartCandleStore"/> write.
/// </summary>
public sealed class CandlePersistenceOptions
{
    public const string SectionName = "Ict:MarketData:Persistence";

    /// <summary>The fewest candles the background writer accumulates before flushing to Postgres.</summary>
    private const int MinBatchSize = 1;

    /// <summary>
    /// The most candles the background writer accumulates per flush.  The channel is bounded to this
    /// capacity so a sustained burst (e.g. a fast replay) back-pressures the producer rather than growing
    /// memory without bound.
    /// </summary>
    private const int MaxBatchSize = 10_000;

    /// <summary>The shortest flush interval (1 ms) — useful in tests that want near-immediate flushing.</summary>
    private const int MinFlushIntervalMs = 1;

    /// <summary>A sane ceiling on the flush interval so candles aren't silently held for many minutes.</summary>
    private const int MaxFlushIntervalMs = 300_000; // 5 minutes

    /// <summary>
    /// How many candles the background writer accumulates before flushing them to Postgres in one batch
    /// INSERT ON CONFLICT DO NOTHING.  A larger batch amortises DB round-trips over a fast replay;
    /// the <see cref="FlushIntervalMs"/> bound ensures a smaller batch is never held indefinitely.
    /// Defaults to 200 (≈3–4 bars per second at M1 = ~1 minute of data per flush — well within Postgres
    /// INSERT bandwidth).
    /// </summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>
    /// The maximum milliseconds between flushes, regardless of how full the batch is.  A slow or idle feed
    /// (e.g. a live poll at 30-second cadence) would never fill a 200-candle batch; the flush interval
    /// ensures recent bars reach Postgres promptly.  Defaults to 5 000 ms (5 seconds).
    /// </summary>
    public int FlushIntervalMs { get; init; } = 5_000;

    /// <summary>
    /// When <c>false</c> (default), candle persistence is disabled: the background writer is NOT started
    /// and no candles are written to the DB.  The in-memory ring buffer (ChartCandleStore) and the CSV
    /// history fallback continue to work as before — a Host with no Postgres or no candle-retention need
    /// runs fine without the persistence background service.  Set to <c>true</c> to enable.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The maximum number of historical candles returned by a time-range query (<c>GetChartRangeQuery</c>),
    /// capping DB reads so a very wide window never pulls the whole table into memory.
    /// Defaults to 10 000 (≈7 days of M1 bars).
    /// </summary>
    public int MaxRangeCandles { get; init; } = 10_000;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (BatchSize is < MinBatchSize or > MaxBatchSize)
        {
            errors.Add($"BatchSize must be within [{MinBatchSize}, {MaxBatchSize}] but was {BatchSize}.");
        }

        if (FlushIntervalMs is < MinFlushIntervalMs or > MaxFlushIntervalMs)
        {
            errors.Add(
                $"FlushIntervalMs must be within [{MinFlushIntervalMs}, {MaxFlushIntervalMs}] but was {FlushIntervalMs}.");
        }

        if (MaxRangeCandles < 1)
        {
            errors.Add($"MaxRangeCandles must be at least 1 but was {MaxRangeCandles}.");
        }

        return errors;
    }
}
