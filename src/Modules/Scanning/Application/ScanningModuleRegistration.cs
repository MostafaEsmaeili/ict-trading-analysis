using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Sessions;
using IctTrader.Scanning.Application.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IctTrader.Scanning.Application;

/// <summary>
/// Composition-root wiring for the Scanning module (plan §3.0a). Registers the stateful scan machinery: the
/// <see cref="ISymbolScannerFactory"/> (builds a scanner from the validated <c>Ict:*</c> options) and the
/// <see cref="ISymbolScannerRegistry"/> (the live per-(symbol, style) cache) as SINGLETONS — the scan FSM state
/// must persist across candles — plus the <see cref="RecentSetupStore"/> SINGLETON: the bounded recent-setup
/// read-model the chart-overlay query handler reads (the §9.1 ICT Pattern Chart overlays). The bus handlers
/// (<see cref="CandleIngestedHandler"/>, <see cref="SetupConfirmedChartProjectionHandler"/>,
/// <see cref="GetRecentSetupsQueryHandler"/>) are NOT registered here: they are auto-discovered by
/// <c>AddMessaging</c>'s assembly scan. The host calls <c>AddIctOptions</c> (the <c>IOptions&lt;T&gt;</c> set the
/// factory depends on) and <c>AddMessaging</c> separately.
/// </summary>
public static class ScanningModuleRegistration
{
    public static IServiceCollection AddScanningModule(this IServiceCollection services)
    {
        // The per-instrument catalog (§2.5.7 FX-vs-index resolution) — shared by Scanning + PaperTrading, so it is
        // registered idempotently (TryAdd) by whichever module wires first. The built-in catalog is pure/immutable.
        services.TryAddSingleton<IInstrumentRegistry>(InstrumentCatalog.Default);
        // The live runtime-settings store the scanner registry watches for changes. TryAdd (empty fallback) so a
        // standalone module/test resolves it; the Host registers the config-seeded one first, which wins.
        services.TryAddSingleton<IRuntimeSettings>(_ => new RuntimeSettings());
        // The economic-calendar store the scanner loads into its MarketContext so the §2.5.2 gate fires. TryAdd
        // (empty fallback, never loaded → the gate stays fail-open) so a standalone module/test resolves it; the
        // Host registers the same singleton and a hosted loader populates it from the configured source.
        services.TryAddSingleton<IEconomicCalendarStore, EconomicCalendarStore>();
        services.AddSingleton<ISymbolScannerFactory, SymbolScannerFactory>();
        services.AddSingleton<ISymbolScannerRegistry, SymbolScannerRegistry>();
        services.AddSingleton<RecentSetupStore>();
        return services;
    }
}
