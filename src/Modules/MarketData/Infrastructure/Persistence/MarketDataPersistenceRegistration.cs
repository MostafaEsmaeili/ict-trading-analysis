using IctTrader.Domain.Repositories;
using IctTrader.MarketData.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.MarketData.Infrastructure.Persistence;

/// <summary>
/// DI extension for the MarketData module's candle persistence layer (plan §7).
/// <para>
/// The caller is responsible for registering <see cref="MarketDataDbContext"/> via
/// <c>AddDbContext&lt;MarketDataDbContext&gt;</c> (with the Npgsql provider and the migrations assembly)
/// BEFORE calling this method — exactly as <c>ScanLoopRegistration.AddScanLoop</c> does for
/// <c>PaperTradingDbContext</c>.  This keeps DI wiring in the composition root (the Host), not in the
/// Infrastructure layer.
/// </para>
/// </summary>
public static class MarketDataPersistenceRegistration
{
    public static IServiceCollection AddMarketDataPersistence(this IServiceCollection services)
    {
        // The repository is Scoped so it shares the per-dispatch DbContext (one connection per bus scope).
        services.AddScoped<ICandleRepository, CandleRepository>();

        return services;
    }
}
