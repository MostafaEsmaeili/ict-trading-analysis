using IctTrader.Domain.Trading;

namespace IctTrader.Domain.Repositories;

/// <summary>
/// Aggregate-scoped repository for <see cref="ArmedEntry"/> (plan §3.0 — no generic repository;
/// collection-like abstraction over the armed_entries table). Updates persist automatically via
/// EF change-tracking on the loaded instance; callers commit via <see cref="IPaperTradingUnitOfWork"/>.
/// </summary>
public interface IArmedEntryRepository
{
    /// <summary>Finds the armed entry with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<ArmedEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages a new armed entry for insertion. The caller commits via <see cref="IPaperTradingUnitOfWork"/>.</summary>
    Task AddAsync(ArmedEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every armed entry whose <see cref="ArmedEntryStatus"/> is <see cref="ArmedEntryStatus.Armed"/>
    /// (resting limits that have not yet been triggered or cancelled) — the per-candle entry-manager warm-start set.
    /// The filter is pushed to SQL so the full table is never loaded into memory.
    /// </summary>
    Task<IReadOnlyList<ArmedEntry>> GetActiveAsync(CancellationToken cancellationToken = default);
}
