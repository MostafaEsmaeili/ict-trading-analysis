using IctTrader.Domain.Trading;

namespace IctTrader.Domain.Repositories;

/// <summary>
/// Aggregate-scoped collection of <see cref="ArmedEntry"/> (plan §3.0 — no generic repository). A loaded
/// armed entry is mutated in place; the caller commits the change through <see cref="IPaperTradingUnitOfWork"/>.
/// </summary>
public interface IArmedEntryRepository
{
    /// <summary>Finds the armed entry with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<ArmedEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages a new armed entry; the caller commits through <see cref="IPaperTradingUnitOfWork"/>.</summary>
    Task AddAsync(ArmedEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every armed entry whose <see cref="ArmedEntryStatus"/> is <see cref="ArmedEntryStatus.Armed"/>
    /// (resting limits not yet triggered or cancelled) — the per-candle entry-manager warm-start set.
    /// </summary>
    Task<IReadOnlyList<ArmedEntry>> GetActiveAsync(CancellationToken cancellationToken = default);
}
