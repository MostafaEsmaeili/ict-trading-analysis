namespace IctTrader.Domain.Repositories;

/// <summary>
/// Unit-of-work boundary for the PaperTrading aggregate cluster (plan §7): commits the changes staged across
/// the three repositories (<see cref="IPaperAccountRepository"/>, <see cref="IPaperTradeRepository"/>,
/// <see cref="IArmedEntryRepository"/>) as a single atomic unit. The persistence-side mechanics (transaction,
/// optimistic-concurrency control) are the implementation's concern, not part of this contract.
/// </summary>
public interface IPaperTradingUnitOfWork
{
    /// <summary>
    /// Commits all staged changes (new aggregates and in-place mutations) atomically. Throws if a concurrent
    /// modification of one of the affected aggregates is detected.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
