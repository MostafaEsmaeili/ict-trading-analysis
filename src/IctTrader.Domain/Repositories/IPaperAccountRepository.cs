using IctTrader.Domain.Trading;

namespace IctTrader.Domain.Repositories;

/// <summary>
/// Aggregate-scoped repository for <see cref="PaperAccount"/> (plan §3.0 — no generic repository;
/// collection-like abstraction over the paper_accounts table). Updates persist automatically via
/// EF change-tracking on the loaded instance; callers commit via <see cref="IPaperTradingUnitOfWork"/>.
/// </summary>
public interface IPaperAccountRepository
{
    /// <summary>Finds the account with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<PaperAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages a new account for insertion. The caller commits via <see cref="IPaperTradingUnitOfWork"/>.</summary>
    Task AddAsync(PaperAccount account, CancellationToken cancellationToken = default);
}
