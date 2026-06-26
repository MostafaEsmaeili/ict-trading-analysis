namespace IctTrader.Domain.Repositories;

/// <summary>
/// Unit-of-work boundary for the PaperTrading aggregate cluster (plan §7). A single
/// <see cref="SaveChangesAsync"/> commits mutations to all three aggregates (<see cref="IPaperAccountRepository"/>,
/// <see cref="IPaperTradeRepository"/>, <see cref="IArmedEntryRepository"/>) in one database transaction,
/// guarded by each aggregate's optimistic-concurrency token (plan §7 conventions). This abstraction is
/// persistence-agnostic; the EF implementation lives in the PaperTrading Infrastructure.
/// <para>
/// The three repositories and the unit of work share the SAME scoped persistence context (DI scope = one bus
/// dispatch), so all staged changes commit atomically here without an explicit distributed transaction.
/// </para>
/// </summary>
public interface IPaperTradingUnitOfWork
{
    /// <summary>
    /// Flushes all pending changes (inserts and in-place aggregate mutations) to the database in a single
    /// round-trip. Throws a concurrency exception if an aggregate's optimistic-concurrency token has changed
    /// under the session (a concurrent update was detected).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
