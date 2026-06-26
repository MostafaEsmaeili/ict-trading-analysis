using IctTrader.Domain.Repositories;
using IctTrader.Domain.Trading;

namespace IctTrader.PaperTrading.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPaperAccountRepository"/> backed by
/// <see cref="PaperTradingDbContext.PaperAccounts"/>. Updates on loaded aggregates are tracked automatically by
/// the change tracker; the caller commits via <see cref="PaperTradingUnitOfWork"/> (plan §7).
/// </summary>
internal sealed class PaperAccountRepository : IPaperAccountRepository
{
    private readonly PaperTradingDbContext _context;

    public PaperAccountRepository(PaperTradingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<PaperAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.PaperAccounts.FindAsync([id], cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(PaperAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        await _context.PaperAccounts.AddAsync(account, cancellationToken);
    }
}
