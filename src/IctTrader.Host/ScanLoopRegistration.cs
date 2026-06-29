using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.MarketData.Application.Abstractions;
using IctTrader.MarketData.Application.Chart;
using IctTrader.MarketData.Application.Persistence;
using IctTrader.MarketData.Infrastructure.Feeds;
using IctTrader.MarketData.Infrastructure.Persistence;
using IctTrader.PaperTrading.Application;
using IctTrader.PaperTrading.Infrastructure;
using IctTrader.PaperTrading.Infrastructure.Persistence;
using IctTrader.Scanning.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IctTrader.Host;

/// <summary>
/// Composes the runnable scan loop (WP7): the PaperTrading EF <c>DbContext</c> on Npgsql, the aggregate-scoped
/// persistence, the Scanning + PaperTrading modules, and a config-selectable, read-only market-data feed driven by
/// a single background ingestion hosted service. This is the seam that finally turns the pure, ICT-gated domain
/// into a running backend — candles flow in through the selected feed, the bus dispatches them to the module
/// handlers, and confirmed advisory setups become paper trades persisted to Postgres.
///
/// <para>The operator picks the feed with <c>Ict:MarketData:Provider</c> (<see cref="MarketFeedProvider.Replay"/>
/// default, or <see cref="MarketFeedProvider.Oanda"/>). BOTH providers are registered as the read-only
/// <see cref="IMarketDataFeed"/> and ingested by ONE <see cref="MarketDataIngestionHostedService"/> — the feed is
/// the ONLY market-data source and is structurally read-only (a candle stream — no order or broker surface
/// anywhere), preserving the NON-NEGOTIABLE no-live-trading guardrail whichever provider is chosen.</para>
/// </summary>
public static class ScanLoopRegistration
{
    public static IServiceCollection AddScanLoop(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // History-fetch mode (issue #100): a one-shot, READ-ONLY backtest CSV exporter that runs INSTEAD of the
        // normal scan loop. When Ict:MarketData:Oanda:FetchHistory is true we register only the OANDA history fetcher
        // + the export hosted service and return early — no DbContext, no ingestion, no scan-loop persistence — so
        // the host fetches candles, writes CSV files, and stops. It writes ONLY local CSV (the guardrail is structural).
        var fetchHistory = configuration.GetSection(OandaFeedOptions.SectionName)
            .GetValue<bool>(nameof(OandaFeedOptions.FetchHistory));
        if (fetchHistory)
        {
            services.AddOandaHistoryFetcher(configuration);
            services.AddHostedService<HistoryFetchHostedService>();
            return services;
        }

        var provider = configuration.GetSection(MarketDataOptions.SectionName)
            .GetValue(nameof(MarketDataOptions.Provider), MarketFeedProvider.Replay);
        var replayEnabled = configuration.GetSection(ReplayFeedOptions.SectionName)
            .GetValue<bool>(nameof(ReplayFeedOptions.Enabled));

        // Ingestion runs whenever a non-Replay provider is selected, or when Replay is explicitly enabled. (Replay
        // is OFF by default so a bare Host stays a pure REST/SignalR surface until an operator opts in.)
        var ingestionWillRun = provider != MarketFeedProvider.Replay || replayEnabled;

        // Fail fast: a running feed persists paper trades, so a missing connection string would crash mid-loop on
        // the first DB write rather than at startup. Refuse to start when ingestion will run without a database —
        // for EITHER provider.
        if (ingestionWillRun && string.IsNullOrWhiteSpace(configuration.GetConnectionString("PaperTrading")))
        {
            throw new InvalidOperationException(
                $"{MarketDataOptions.SectionName}:Provider is '{provider}' (ingestion will run) but " +
                "ConnectionStrings:PaperTrading is not set. The scan loop persists paper trades and needs a " +
                "database — set the connection string, or select Replay and leave it disabled to stay idle.");
        }

        // The PaperTrading write-model context on Npgsql. A null/empty connection string is fine at registration
        // when ingestion is OFF — the context is lazy and only opens a connection when a handler reads or writes
        // (so a bare Host boots even before a database is provisioned). The migrations assembly mirrors the
        // design-time factory so `dotnet ef` and the runtime resolve the same migrations.
        services.AddDbContext<PaperTradingDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("PaperTrading"),
                npgsql => npgsql.MigrationsAssembly(typeof(PaperTradingDbContext).Assembly.FullName)));

        services.AddPaperTradingPersistence();

        // The MarketData candle read-model context (plan §7 — a SEPARATE context so MarketData.Infrastructure
        // never references PaperTrading.Infrastructure; the arch boundary stays clean). Both contexts connect
        // to the SAME Postgres database (the shared ConnectionStrings:PaperTrading connection string); the
        // candles table is created by its own AddCandles migration (dotnet ef migrations add AddCandles
        // --project src/Modules/MarketData/Infrastructure --startup-project src/IctTrader.Host).
        services.AddDbContext<MarketDataDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("PaperTrading"),
                npgsql => npgsql.MigrationsAssembly(typeof(MarketDataDbContext).Assembly.FullName)));

        // Candle persistence: repository + the batched dual-write background service (plan §7).
        // The bounded channel is ALWAYS registered so AddMessaging's assembly-scan can resolve
        // CandlePersistenceProjectionHandler regardless of the Enabled flag.  The background writer
        // (CandlePersistenceHostedService) is started only when Enabled=true; when it is not running the
        // channel fills up to BatchSize and silently drops old candles (DropOldest) — the scan dispatch is
        // never blocked. Default Enabled=false so a Host with no retention need runs with zero DB I/O.
        var persistenceOptions = configuration
            .GetSection(CandlePersistenceOptions.SectionName)
            .Get<CandlePersistenceOptions>() ?? new CandlePersistenceOptions();

        // Bounded channel — always present; capacity = BatchSize so an idle (disabled) channel never grows.
        var channel = System.Threading.Channels.Channel.CreateBounded<Domain.ValueObjects.Candle>(
            new System.Threading.Channels.BoundedChannelOptions(persistenceOptions.BatchSize)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        services.AddSingleton(channel);

        // The CandlePersistenceConfiguration bridges MaxRangeCandles into the Application-layer range query
        // handler (GetChartRangeQueryHandler) without pulling IOptions into the Application project.
        services.AddSingleton(new CandlePersistenceConfiguration(persistenceOptions.MaxRangeCandles));

        if (persistenceOptions.Enabled)
        {
            services.AddMarketDataPersistence();

            // The CandlePersistenceBatchOptions bridges BatchSize + FlushIntervalMs into the hosted service.
            services.AddSingleton(new CandlePersistenceBatchOptions(
                persistenceOptions.BatchSize, persistenceOptions.FlushIntervalMs));

            services.AddHostedService<CandlePersistenceHostedService>();
        }
        else
        {
            // Register a no-op repository so the GetChartRangeQueryHandler (auto-discovered by AddMessaging)
            // resolves even when persistence is disabled. It returns empty collections; the REST endpoint then
            // falls back to the CSV history (ChartHistory).
            services.AddScoped<Domain.Repositories.ICandleRepository, NoCandleRepository>();
        }

        // Runtime settings (plan §15): the mutable per-instrument overrides the operator edits live. Seeded ONCE from
        // Ict:Instruments, then updated via the settings API; its revision ticks on every change so the per-symbol
        // scanner/orchestrator caches rebuild with the new options (live apply, no restart). Registered (TryAdd, so the
        // modules' empty fallback is a no-op) BEFORE the modules so every consumer shares this seeded instance.
        services.TryAddSingleton<IRuntimeSettings>(sp =>
            new RuntimeSettings(sp.GetRequiredService<IOptions<InstrumentOverridesOptions>>().Value.Overrides));

        // The config-augmenting registry overlays those LIVE per-instrument settings (e.g. NAS100's required subset) on
        // the built-in catalog. Registered BEFORE the modules so their TryAddSingleton(InstrumentCatalog.Default) is a
        // no-op and EVERY consumer — scanner, trade orchestrator, backtest engine — resolves THIS config-aware registry.
        services.AddSingleton<IInstrumentRegistry>(sp => new ConfigurableInstrumentRegistry(
            InstrumentCatalog.Default, sp.GetRequiredService<IRuntimeSettings>()));

        services.AddScanningModule();
        services.AddPaperTradingModule();

        // The PaperTrading-backed take-state enricher for the Scanning signals feed (plan §15 TAKE workflow). The Host
        // references both modules, so the cross-module enrichment (effective Auto/Manual entry mode + the pending board +
        // whether a trade exists) is wired here; the Scanning SignalRankingService resolves it through its port. Read-only.
        services.AddSingleton<IctTrader.Scanning.Application.Signals.ISignalTakeStateProvider,
            PaperTradingSignalTakeStateProvider>();

        // Bind the feed-provider selector (no magic strings) and wire the chosen READ-ONLY feed as IMarketDataFeed.
        services.AddOptions<MarketDataOptions>()
            .Bind(configuration.GetSection(MarketDataOptions.SectionName))
            .ValidateOnStart();

        AddSelectedFeed(services, configuration, provider, replayEnabled);

        // ONE hosted service ingests whatever IMarketDataFeed was registered. It only runs when ingestion is on; a
        // disabled Replay feed registers nothing and leaves the scanner idle (the default, pure-API posture).
        if (ingestionWillRun)
        {
            services.AddHostedService<MarketDataIngestionHostedService>();
        }

        return services;
    }

    /// <summary>
    /// Registers the selected read-only feed as <see cref="IMarketDataFeed"/>. Replay registers a singleton built
    /// from the CSV fixture (loaded lazily on first resolve, INSIDE the hosted service's try/catch — a bad path is
    /// logged, not fatal); OANDA delegates to <c>AddOandaFeed</c> (a typed-HttpClient feed validated at startup).
    /// When Replay is selected but disabled NOTHING is registered — the hosted service is not added either, so the
    /// Host stays a pure REST/SignalR surface.
    /// </summary>
    private static void AddSelectedFeed(
        IServiceCollection services,
        IConfiguration configuration,
        MarketFeedProvider provider,
        bool replayEnabled)
    {
        switch (provider)
        {
            case MarketFeedProvider.Replay:
                // The replay feed config. ValidateOnStart fails fast on a self-contradictory setup (Enabled with no
                // fixture path) rather than running a silently idle scanner.
                services.AddOptions<ReplayFeedOptions>()
                    .Bind(configuration.GetSection(ReplayFeedOptions.SectionName))
                    .Validate(
                        options => !options.Enabled || !string.IsNullOrWhiteSpace(options.FixturePath),
                        $"{ReplayFeedOptions.SectionName}: FixturePath is required when the replay feed is Enabled.")
                    .ValidateOnStart();

                if (replayEnabled)
                {
                    // Register the feed as a singleton built from the CSV fixture. The factory runs the FIRST time
                    // the hosted service resolves IMarketDataFeed (inside its try/catch), so a bad/missing fixture
                    // path is caught + logged there rather than crashing host startup.
                    services.AddSingleton<IMarketDataFeed>(sp =>
                    {
                        var options = sp.GetRequiredService<IOptions<ReplayFeedOptions>>().Value;
                        return new ReplayMarketDataFeed(CsvCandleSource.Load(options.FixturePath!));
                    });
                }

                break;

            case MarketFeedProvider.Oanda:
                // The OANDA-practice feed: a typed-HttpClient singleton registered as IMarketDataFeed, with its own
                // ValidateOnStart that fails fast on a blank token.
                services.AddOandaFeed(configuration);
                break;

            default:
                throw new InvalidOperationException(
                    $"{MarketDataOptions.SectionName}:Provider '{provider}' is not a supported market-data feed.");
        }
    }
}
