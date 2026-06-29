using IctTrader.Domain.Repositories;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Host;

/// <summary>
/// A no-op <see cref="ICandleRepository"/> used when candle persistence is disabled
/// (<c>Ict:MarketData:Persistence:Enabled=false</c>, the default).
/// <para>
/// Registered so that <see cref="IctTrader.MarketData.Application.Chart.GetChartRangeQueryHandler"/> (auto-
/// discovered by <c>AddMessaging</c>'s assembly scan) resolves without a DB connection: all reads return empty
/// collections and <see cref="AppendAsync"/> is a no-op.  The REST endpoint's range branch falls back to the
/// CSV history (<c>ChartHistory</c>) when the collections are empty — the same behaviour as when a requested
/// (symbol, timeframe) series has no persisted data yet.
/// </para>
/// <para>
/// Read-model only (plan §6.3 guardrail): no write path reaches an order surface.
/// </para>
/// </summary>
internal sealed class NoCandleRepository : ICandleRepository
{
    public Task<IReadOnlyList<Candle>> GetRangeAsync(
        Symbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        int max,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Candle>>([]);

    public Task<IReadOnlyList<Candle>> GetRecentAsync(
        Symbol symbol,
        Timeframe timeframe,
        int count,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Candle>>([]);

    public Task AppendAsync(IReadOnlyList<Candle> batch, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
