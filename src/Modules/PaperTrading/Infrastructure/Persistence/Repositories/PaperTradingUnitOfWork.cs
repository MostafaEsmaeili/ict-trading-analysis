using IctTrader.Domain.Repositories;

namespace IctTrader.PaperTrading.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPaperTradingUnitOfWork"/> — delegates to
/// <see cref="PaperTradingDbContext.SaveChangesAsync(CancellationToken)"/>, which commits all pending
/// change-tracked mutations for the three PaperTrading aggregates (<see cref="Domain.Trading.PaperAccount"/>,
/// <see cref="Domain.Trading.PaperTrade"/>, <see cref="Domain.Trading.ArmedEntry"/>) in a single database
/// round-trip guarded by each aggregate's <c>xmin</c> optimistic-concurrency token (plan §7).
/// </summary>
internal sealed class PaperTradingUnitOfWork : IPaperTradingUnitOfWork
{
    private readonly PaperTradingDbContext _context;

    public PaperTradingUnitOfWork(PaperTradingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);
}
