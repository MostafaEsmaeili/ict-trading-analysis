using IctTrader.Domain.Trading;

namespace IctTrader.Domain.Repositories;

/// <summary>
/// Aggregate-scoped repository for <see cref="PaperTrade"/> (plan §3.0 — no generic repository;
/// collection-like abstraction over the paper_trades table). Updates persist automatically via
/// EF change-tracking on the loaded instance; callers commit via <see cref="IPaperTradingUnitOfWork"/>.
/// </summary>
public interface IPaperTradeRepository
{
    /// <summary>Finds the trade with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<PaperTrade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages a new trade for insertion. The caller commits via <see cref="IPaperTradingUnitOfWork"/>.</summary>
    Task AddAsync(PaperTrade trade, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every trade whose <see cref="TradeStatus"/> is <see cref="TradeStatus.Open"/> — the active-trades
    /// read-side and the warm-start set the orchestrator needs on reload. The filter is pushed to SQL so the full
    /// table is never loaded into memory.
    /// </summary>
    Task<IReadOnlyList<PaperTrade>> GetOpenAsync(CancellationToken cancellationToken = default);
}
