using IctTrader.Domain.Repositories;
using IctTrader.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace IctTrader.MarketData.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICandleRepository"/> (plan §7).
/// <para>
/// <b>UPSERT via raw SQL:</b> EF Core 10 does not expose a native "INSERT ON CONFLICT DO NOTHING" API.
/// The repository calls <c>ExecuteSqlRawAsync</c> with a parameterised Postgres statement
/// <c>INSERT INTO candles … ON CONFLICT (symbol, timeframe, open_time_utc) DO NOTHING</c> so a
/// replayed or redelivered <see cref="Domain.Contracts.CandleIngested"/> is a true DB-level no-op (the
/// surrogate PK sequence is never incremented for skipped rows, unlike an EF upsert that reads first).
/// </para>
/// <para>
/// <b>Read-model only (plan §6.3):</b> there is no delete and no update path. The returned domain
/// <see cref="Candle"/> VOs are constructed from the entity columns; they are pure value objects and
/// route nowhere.
/// </para>
/// </summary>
internal sealed class CandleRepository(MarketDataDbContext dbContext) : ICandleRepository
{
    private readonly MarketDataDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Candle>> GetRangeAsync(
        Symbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        int max,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max);

        var sym = symbol.Value;
        var tf = timeframe.ToString();

        // Normalise to UTC so Npgsql's timestamptz comparison is unambiguous.
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        var entities = await _dbContext.Candles
            .AsNoTracking()
            .Where(c => c.Symbol == sym && c.Timeframe == tf
                        && c.OpenTimeUtc >= fromUtc && c.OpenTimeUtc <= toUtc)
            .OrderBy(c => c.OpenTimeUtc)
            .Take(max)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(ToCandle).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Candle>> GetRecentAsync(
        Symbol symbol,
        Timeframe timeframe,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var sym = symbol.Value;
        var tf = timeframe.ToString();

        // Fetch the newest `count` rows by descending open_time_utc (index scan), then reverse in-memory.
        var entities = await _dbContext.Candles
            .AsNoTracking()
            .Where(c => c.Symbol == sym && c.Timeframe == tf)
            .OrderByDescending(c => c.OpenTimeUtc)
            .Take(count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Restore chronological (oldest→newest) order — the chart and the warm-start path expect it.
        entities.Reverse();
        return entities.Select(ToCandle).ToList();
    }

    /// <inheritdoc/>
    public async Task AppendAsync(IReadOnlyList<Candle> batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0)
            return;

        // Build a multi-row INSERT ON CONFLICT DO NOTHING.  Parameterised to prevent SQL injection;
        // EF Core's ExecuteSqlRawAsync uses POSITIONAL {0},{1},… placeholders (it converts each to a
        // @p0,@p1,… DbParameter mapped to the object[] in order) — NOT @-named placeholders. We emit
        // eight {n} placeholders per candle, in the column order (symbol, timeframe, open_time_utc,
        // open, high, low, close, volume), each CAST so Postgres binds the exact column type.
        var paramList = new List<object>(batch.Count * 8);
        var valueClauses = new List<string>(batch.Count);

        for (var i = 0; i < batch.Count; i++)
        {
            var c = batch[i];
            var idx = i * 8;
            valueClauses.Add(
                $"(CAST({{{idx}}} AS varchar(20)), CAST({{{idx + 1}}} AS varchar(10)), " +
                $"CAST({{{idx + 2}}} AS timestamptz), CAST({{{idx + 3}}} AS numeric(18,8)), CAST({{{idx + 4}}} AS numeric(18,8)), " +
                $"CAST({{{idx + 5}}} AS numeric(18,8)), CAST({{{idx + 6}}} AS numeric(18,8)), " +
                $"CAST({{{idx + 7}}} AS numeric(18,8)))");
            paramList.Add(c.Symbol.Value);
            paramList.Add(c.Timeframe.ToString());
            paramList.Add(c.OpenTimeUtc.UtcDateTime);   // pass as DateTime UTC so Npgsql writes timestamptz correctly
            paramList.Add(c.Open);
            paramList.Add(c.High);
            paramList.Add(c.Low);
            paramList.Add(c.Close);
            paramList.Add(c.Volume);
        }

        var sql =
            "INSERT INTO candles (symbol, timeframe, open_time_utc, open, high, low, close, volume) VALUES " +
            string.Join(", ", valueClauses) +
            " ON CONFLICT (symbol, timeframe, open_time_utc) DO NOTHING";

        await _dbContext.Database
            .ExecuteSqlRawAsync(sql, paramList, cancellationToken)
            .ConfigureAwait(false);
    }

    // ── Domain VO factory ─────────────────────────────────────────────────────────────────────────────

    private static Candle ToCandle(CandleEntity e)
    {
        var symbol = new Symbol(e.Symbol);

        // The timeframe column stores the enum member name (e.g. "M5") — parse it back.
        if (!Enum.TryParse<Timeframe>(e.Timeframe, ignoreCase: false, out var tf))
        {
            throw new InvalidOperationException(
                $"Persisted timeframe '{e.Timeframe}' for {e.Symbol} at {e.OpenTimeUtc:O} is not a known Timeframe " +
                "enum member. This indicates a data migration issue.");
        }

        // open_time_utc is stored as timestamptz and round-trips as UTC; guard defensively.
        var openUtc = e.OpenTimeUtc.ToUniversalTime();

        return new Candle(symbol, tf, openUtc, e.Open, e.High, e.Low, e.Close, e.Volume);
    }
}
