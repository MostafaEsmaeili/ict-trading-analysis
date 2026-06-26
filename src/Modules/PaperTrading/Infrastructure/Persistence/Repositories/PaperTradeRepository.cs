using IctTrader.Domain.Repositories;
using IctTrader.Domain.Trading;
using Microsoft.EntityFrameworkCore;

namespace IctTrader.PaperTrading.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPaperTradeRepository"/> backed by
/// <see cref="PaperTradingDbContext.PaperTrades"/>. Updates on loaded aggregates are tracked automatically by the
/// change tracker; the caller commits via <see cref="PaperTradingUnitOfWork"/> (plan §7).
/// </summary>
internal sealed class PaperTradeRepository : IPaperTradeRepository
{
    private readonly PaperTradingDbContext _context;

    public PaperTradeRepository(PaperTradingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<PaperTrade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.PaperTrades.FindAsync([id], cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(PaperTrade trade, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trade);
        await _context.PaperTrades.AddAsync(trade, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The <see cref="TradeStatus.Open"/> filter is a single string-equality predicate pushed to SQL via the
    /// enum-as-string column mapping (plan §7); the full paper_trades table is never loaded into memory.
    /// </remarks>
    public async Task<IReadOnlyList<PaperTrade>> GetOpenAsync(CancellationToken cancellationToken = default)
        => await _context.PaperTrades
            .Where(t => t.Status == TradeStatus.Open)
            .ToListAsync(cancellationToken);
}
