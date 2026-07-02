using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Styles;
using IctTrader.Scanning.Application.Scanning;
using IctTrader.Scanning.Application.Scanning.Models;
using IctTrader.Scanning.Application.Signals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IctTrader.Scanning.Application;

/// <summary>
/// Composition-root wiring for the Scanning module (plan §3.0a). Registers the stateful scan machinery: the
/// <see cref="ISymbolScannerFactory"/> (builds a scanner from the validated <c>Ict:*</c> options) and the
/// <see cref="ISymbolScannerRegistry"/> (the live per-(symbol, style) cache) as SINGLETONS — the scan FSM state
/// must persist across candles — plus the <see cref="RecentSetupStore"/> SINGLETON: the bounded recent-setup
/// read-model the chart-overlay query handler reads (the §9.1 ICT Pattern Chart overlays) — and the signals feed
/// SINGLETONS (<see cref="SignalFeedStore"/>, <see cref="SignalRanker"/>, <see cref="SignalRankingService"/>): the
/// cross-matrix "best opportunities" feed (plan §9). The bus handlers (<see cref="CandleIngestedHandler"/>,
/// <see cref="SetupConfirmedChartProjectionHandler"/>, <see cref="GetRecentSetupsQueryHandler"/>,
/// <see cref="SetupConfirmedSignalFeedHandler"/>, <see cref="GetSignalsQueryHandler"/>) are NOT registered here: they
/// are auto-discovered by <c>AddMessaging</c>'s assembly scan. The host calls <c>AddIctOptions</c> (the
/// <c>IOptions&lt;T&gt;</c> set the factory depends on) and <c>AddMessaging</c> separately.
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
        // The setup-model catalog (plan §16): every implemented model's pipeline recipe + option preset, keyed by
        // SetupModel. Pure/immutable code presets — the scanner factory resolves the requested model from it.
        services.TryAddSingleton(SetupModelCatalog.Default);
        services.AddSingleton<ISymbolScannerFactory, SymbolScannerFactory>();
        services.AddSingleton<ISymbolScannerRegistry, SymbolScannerRegistry>();
        services.AddSingleton<RecentSetupStore>();
        // The live "engine view" geometry read-model (plan §9.1): the CandleIngestedHandler writes the latest snapshot
        // per (symbol, timeframe); GetGeometryOverlaysQueryHandler (auto-discovered by AddMessaging) reads it.
        services.AddSingleton<GeometryOverlayStore>();
        // The "best opportunities" signals feed (plan §9): the bounded cross-matrix store, the pure-domain ranker, and
        // the read-side ranking service — all SINGLETONS so the feed survives across bus dispatches. They consume the
        // validated Ict:Signals options the Host binds via AddIctOptions; a standalone module/test that did not bind
        // them falls back to the verified defaults (Options.Create) so resolution never fails. The feed handlers
        // (SetupConfirmedSignalFeedHandler, GetSignalsQueryHandler) are auto-discovered by AddMessaging's assembly scan.
        services.AddSingleton(sp =>
            new SignalFeedStore(sp.GetService<IOptions<SignalRankingOptions>>()?.Value ?? new SignalRankingOptions()));
        services.AddSingleton(sp =>
            new SignalRanker(sp.GetService<IOptions<SignalRankingOptions>>()?.Value ?? new SignalRankingOptions()));
        services.AddSingleton(sp => new SignalRankingService(
            sp.GetRequiredService<SignalFeedStore>(),
            sp.GetRequiredService<SignalRanker>(),
            sp.GetService<IOptions<SignalRankingOptions>>()?.Value ?? new SignalRankingOptions(),
            // The PaperTrading-backed take-state enricher (plan §15). The Host registers the adapter; a standalone
            // Scanning module/test resolves the no-op default so every signal stays in its takeable-unknown wire default.
            sp.GetService<ISignalTakeStateProvider>()));
        // The pure matrix router the CandleIngestedHandler uses to scan each active style on its canonical entry TF
        // (plan §4.7). It resolves the per-style entry timeframe from the validated Ict:TradeStyles options via the
        // TradeStyleClassifier domain service — no hardcoded TF.
        services.AddSingleton(sp =>
            new StyleTimeframeMap(
                new TradeStyleClassifier(sp.GetRequiredService<IOptions<TradeStyleOptions>>().Value)));
        return services;
    }
}
