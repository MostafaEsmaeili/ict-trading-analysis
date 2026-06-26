using IctTrader.Domain.Trading;

namespace IctTrader.Domain.Repositories;

/// <summary>
/// Aggregate-scoped collection of <see cref="PaperTrade"/> (plan §3.0 — no generic repository). A loaded trade
/// is mutated in place; the caller commits the change through <see cref="IPaperTradingUnitOfWork"/>.
/// </summary>
public interface IPaperTradeRepository
{
    /// <summary>Finds the trade with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<PaperTrade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages a new trade; the caller commits through <see cref="IPaperTradingUnitOfWork"/>.</summary>
    Task AddAsync(PaperTrade trade, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every trade whose <see cref="TradeStatus"/> is <see cref="TradeStatus.Open"/> — the active-trades
    /// read-side and the warm-start set the orchestrator needs on reload.
    /// </summary>
    Task<IReadOnlyList<PaperTrade>> GetOpenAsync(CancellationToken cancellationToken = default);
}
