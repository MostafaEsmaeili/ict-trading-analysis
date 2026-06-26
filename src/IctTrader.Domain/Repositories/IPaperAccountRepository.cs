using IctTrader.Domain.Trading;

namespace IctTrader.Domain.Repositories;

/// <summary>
/// Aggregate-scoped collection of <see cref="PaperAccount"/> (plan §3.0 — no generic repository). A loaded
/// account is mutated in place; the caller commits the change through <see cref="IPaperTradingUnitOfWork"/>.
/// </summary>
public interface IPaperAccountRepository
{
    /// <summary>Finds the account with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<PaperAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages a new account; the caller commits through <see cref="IPaperTradingUnitOfWork"/>.</summary>
    Task AddAsync(PaperAccount account, CancellationToken cancellationToken = default);
}
