using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Repositories;

/// <summary>
/// Aggregate-scoped read-model repository for historical OHLC candles (plan §7 — no generic repository).
/// <para>
/// <b>Read-model only (plan §6.3 guardrail):</b> candles flow in through <see cref="AppendAsync"/> (an
/// idempotent UPSERT on the natural key) and are served via range/recent reads. There is no delete, no
/// update, and no order path anywhere near this interface — appending an OHLC bar routes nowhere near a
/// broker (the guardrail is structural). Returns BCL types and the domain <see cref="Candle"/> VO only so
/// the Domain assembly remains dependency-free.
/// </para>
/// </summary>
public interface ICandleRepository
{
    /// <summary>
    /// Returns at most <paramref name="max"/> candles for the given symbol/timeframe whose
    /// <see cref="Candle.OpenTimeUtc"/> falls in [<paramref name="from"/>, <paramref name="to"/>], ordered
    /// chronologically (oldest→newest).  <paramref name="max"/> caps the row count so a very wide window
    /// never pulls the whole table into memory.
    /// </summary>
    Task<IReadOnlyList<Candle>> GetRangeAsync(
        Symbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        int max,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <paramref name="count"/> most-recent candles for the given symbol/timeframe, ordered
    /// chronologically (oldest→newest) — the live-tail read the chart ring-buffer warm-start and the
    /// time-range fallback use.
    /// </summary>
    Task<IReadOnlyList<Candle>> GetRecentAsync(
        Symbol symbol,
        Timeframe timeframe,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent UPSERT of a batch of candles.  The natural key is <c>(symbol, timeframe, open_time_utc)</c>;
    /// a candle that already exists in the table is silently skipped so a replayed or redelivered
    /// <c>CandleIngested</c> event is a no-op (plan §7 idempotent ingestion convention).
    /// </summary>
    Task AppendAsync(IReadOnlyList<Candle> batch, CancellationToken cancellationToken = default);
}
