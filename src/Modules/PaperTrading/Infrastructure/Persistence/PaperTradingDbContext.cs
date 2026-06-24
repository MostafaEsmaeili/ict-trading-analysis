using IctTrader.Domain.Trading;
using IctTrader.PaperTrading.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace IctTrader.PaperTrading.Infrastructure.Persistence;

/// <summary>
/// The EF Core database context for the PaperTrading module (plan §7).
/// <para>
/// Owns three aggregate roots: <see cref="PaperAccount"/>, <see cref="PaperTrade"/>, and
/// <see cref="ArmedEntry"/>.  Each is mapped to its own table via its respective
/// <see cref="IEntityTypeConfiguration{T}"/> in the <c>Configurations</c> folder.  All timestamps are
/// <c>timestamptz</c> (UTC-aware); prices are <c>numeric(18,8)</c>; currency is <c>numeric(18,2)</c>;
/// enums are stored as strings; JSONB is used for the fill-leg ledger, the reservation ledger, the trade
/// plan, and the setup snapshot (plan §7 conventions).
/// </para>
/// <para>
/// Optimistic concurrency is provided by the Postgres <c>xmin</c> system column via
/// <c>UseXminAsConcurrencyToken()</c> configured on each aggregate entity.
/// </para>
/// </summary>
public sealed class PaperTradingDbContext : DbContext
{
    public PaperTradingDbContext(DbContextOptions<PaperTradingDbContext> options)
        : base(options)
    {
    }

    public DbSet<PaperAccount> PaperAccounts => Set<PaperAccount>();

    public DbSet<PaperTrade> PaperTrades => Set<PaperTrade>();

    public DbSet<ArmedEntry> ArmedEntries => Set<ArmedEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaperAccountConfiguration());
        modelBuilder.ApplyConfiguration(new PaperTradeConfiguration());
        modelBuilder.ApplyConfiguration(new ArmedEntryConfiguration());
    }
}
