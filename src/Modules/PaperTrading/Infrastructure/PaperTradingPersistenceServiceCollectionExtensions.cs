using IctTrader.Domain.Repositories;
using IctTrader.PaperTrading.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.PaperTrading.Infrastructure;

/// <summary>
/// Extension method that registers the PaperTrading persistence layer into the DI container (plan §7).
/// <para>
/// Registers the three aggregate-scoped repositories and the unit-of-work as <b>Scoped</b> services, so they
/// share the same <c>PaperTradingDbContext</c> instance within a single bus-dispatch scope and changes to all
/// three aggregates commit atomically through <see cref="IPaperTradingUnitOfWork.SaveChangesAsync"/>. The
/// <c>PaperTradingDbContext</c> itself is deliberately NOT registered here — that requires the database
/// connection string, which is Host/slice-2e wiring; this extension assumes the context is provided by the
/// caller.
/// </para>
/// </summary>
public static class PaperTradingPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PaperTrading repositories and unit-of-work as Scoped services. Assumes
    /// <c>PaperTradingDbContext</c> is already registered in <paramref name="services"/> by the caller (the Host).
    /// </summary>
    public static IServiceCollection AddPaperTradingPersistence(this IServiceCollection services)
    {
        services.AddScoped<IPaperAccountRepository, PaperAccountRepository>();
        services.AddScoped<IPaperTradeRepository, PaperTradeRepository>();
        services.AddScoped<IArmedEntryRepository, ArmedEntryRepository>();
        services.AddScoped<IPaperTradingUnitOfWork, PaperTradingUnitOfWork>();

        return services;
    }
}
