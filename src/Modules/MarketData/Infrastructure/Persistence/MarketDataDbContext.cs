using IctTrader.MarketData.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace IctTrader.MarketData.Infrastructure.Persistence;

/// <summary>
/// The EF Core database context for the MarketData module's candle persistence (plan §7).
/// <para>
/// Owns ONE entity: <see cref="CandleEntity"/>, mapped to the <c>candles</c> table.  This context is
/// deliberately SEPARATE from <c>PaperTradingDbContext</c> — MarketData.Infrastructure must NOT reference
/// PaperTrading.Infrastructure (a cross-module Infra→Infra reference fails <c>ArchitectureRulesTests</c>).
/// Both contexts connect to the SAME Postgres database via the shared <c>ConnectionStrings:PaperTrading</c>
/// connection string; each owns its own migrations assembly, its own table(s), and its own schema partition
/// (both in <c>public</c>).
/// </para>
/// <para>
/// Candles are immutable once written — there is no concurrency token, no update path, and no delete path.
/// The write model is append-only (INSERT ON CONFLICT DO NOTHING via EF's <c>ExecuteSqlRawAsync</c> in the
/// repository); the context's <c>SaveChangesAsync</c> is used only by the design-time factory and migrations.
/// </para>
/// </summary>
public sealed class MarketDataDbContext : DbContext
{
    public MarketDataDbContext(DbContextOptions<MarketDataDbContext> options)
        : base(options)
    {
    }

    internal DbSet<CandleEntity> Candles => Set<CandleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CandleConfiguration());
    }
}
