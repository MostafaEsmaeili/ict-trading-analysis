using IctTrader.Scanning.Application.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.Scanning.Application;

/// <summary>
/// Composition-root wiring for the Scanning module (plan §3.0a). Registers the stateful scan machinery: the
/// <see cref="ISymbolScannerFactory"/> (builds a scanner from the validated <c>Ict:*</c> options) and the
/// <see cref="ISymbolScannerRegistry"/> (the live per-(symbol, style) cache) as SINGLETONS — the scan FSM state
/// must persist across candles. The bus handler (<see cref="CandleIngestedHandler"/>) is NOT registered here:
/// it is auto-discovered by <c>AddMessaging</c>'s assembly scan as a scoped <c>IEventHandler</c>. The host calls
/// <c>AddIctOptions</c> (the <c>IOptions&lt;T&gt;</c> set the factory depends on) and <c>AddMessaging</c>
/// separately.
/// </summary>
public static class ScanningModuleRegistration
{
    public static IServiceCollection AddScanningModule(this IServiceCollection services)
    {
        services.AddSingleton<ISymbolScannerFactory, SymbolScannerFactory>();
        services.AddSingleton<ISymbolScannerRegistry, SymbolScannerRegistry>();
        return services;
    }
}
