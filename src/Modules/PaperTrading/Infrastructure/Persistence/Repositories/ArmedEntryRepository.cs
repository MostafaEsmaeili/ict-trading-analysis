using IctTrader.Domain.Repositories;
using IctTrader.Domain.Trading;
using Microsoft.EntityFrameworkCore;

namespace IctTrader.PaperTrading.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IArmedEntryRepository"/> backed by
/// <see cref="PaperTradingDbContext.ArmedEntries"/>. Updates on loaded aggregates are tracked automatically by
/// the change tracker; the caller commits via <see cref="PaperTradingUnitOfWork"/> (plan §7).
/// </summary>
internal sealed class ArmedEntryRepository : IArmedEntryRepository
{
    private readonly PaperTradingDbContext _context;

    public ArmedEntryRepository(PaperTradingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<ArmedEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.ArmedEntries.FindAsync([id], cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(ArmedEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _context.ArmedEntries.AddAsync(entry, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The <see cref="ArmedEntryStatus.Armed"/> filter is a single string-equality predicate pushed to SQL via the
    /// enum-as-string column mapping (plan §7); the full armed_entries table is never loaded into memory.
    /// </remarks>
    public async Task<IReadOnlyList<ArmedEntry>> GetActiveAsync(CancellationToken cancellationToken = default)
        => await _context.ArmedEntries
            .Where(e => e.Status == ArmedEntryStatus.Armed)
            .ToListAsync(cancellationToken);
}
