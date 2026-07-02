using System.Globalization;
using IctTrader.Alerting.Application;
using IctTrader.Alerting.Contracts;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Host;
using IctTrader.Host.Backtesting;
using IctTrader.Host.Calendar;
using IctTrader.Host.Hubs;
using IctTrader.MarketData.Application.Chart;
using IctTrader.MarketData.Contracts;
using IctTrader.PaperTrading.Application;
using IctTrader.PaperTrading.Application.Trading;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Performance.Application;
using IctTrader.Performance.Contracts;
using IctTrader.Scanning.Application.Scanning.Models;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Time: the BCL TimeProvider is the single source of time (plan §4.8). Detectors/handlers inject it and
// tests swap in FakeTimeProvider; NY-session math (NyClock, WP1) layers America/New_York over this so
// killzone classification is identical on any host.
builder.Services.AddSingleton(TimeProvider.System);

// Structural live-trading guardrail (plan §0/§6.3): the flag exists only to be asserted false.
builder.Services
    .AddOptions<DefensiveOptions>()
    .Bind(builder.Configuration.GetSection(DefensiveOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<DefensiveOptions>, DefensiveOptionsValidator>();

// Every Ict:* options POCO is bound to its config section and self-validated at startup (plan §4.6, WP7) — a
// mis-configured host fails fast with the section-qualified reason rather than silently mis-running the model.
builder.Services.AddIctOptions(builder.Configuration);

// Fetch-mode (Ict:MarketData:Oanda:FetchHistory) is a STANDALONE one-shot history exporter: it wires only the
// OANDA fetcher + the fetch hosted service (AddScanLoop returns early) and writes a CSV, then stops the app. We
// deliberately skip the bus, the module handlers, and the modules in this mode — those handlers reference
// scan-loop services that fetch-mode never wires, so registering them would fail DI validation at startup.
var fetchHistoryMode = builder.Configuration.GetValue<bool>("Ict:MarketData:Oanda:FetchHistory");

if (fetchHistoryMode)
{
    // Register the bus singleton (no module handlers) so the bus-typed API endpoints still BIND at startup — the
    // fetch tool stops the app after writing the CSV, so they are never actually served. AddScanLoop wires only the
    // OANDA fetcher + its one-shot hosted service in this mode.
    builder.Services.AddMessaging();
    builder.Services.AddScanLoop(builder.Configuration);
}
else
{
    // In-memory message bus (plan §3.0a) — the only inter-module seam. The bus singleton is registered with the
    // module Application assemblies so their handlers are Scrutor-scanned in: MarketData's chart-candle projection
    // + chart-candles query, Scanning's CandleIngestedHandler + recent-setup chart projection + recent-setups
    // query, PaperTrading's SetupConfirmedHandler + candle handler, Performance's PaperTradeClosedHandler + query
    // handlers, and Alerting's setup/trade alert handlers + recent-alerts query — closing the
    // candle→scan→paper-trade→performance chain and feeding the dashboard's Alerts feed + ICT Pattern Chart.
    // The HOST assembly is included too so the bus → SignalR bridge handlers (Realtime/*Broadcaster) are scanned
    // in and push each event to the dashboard live over the push-only TradingHub (plan §9, WP7 SignalR slice).
    builder.Services.AddMessaging(
        typeof(IctTrader.MarketData.Application.Chart.ChartCandleProjectionHandler).Assembly,
        typeof(IctTrader.Scanning.Application.Scanning.CandleIngestedHandler).Assembly,
        typeof(IctTrader.PaperTrading.Application.Trading.SetupConfirmedHandler).Assembly,
        typeof(IctTrader.Performance.Application.PaperTradeClosedHandler).Assembly,
        typeof(SetupConfirmedAlertHandler).Assembly,
        typeof(IctTrader.Host.Realtime.CandleIngestedBroadcaster).Assembly);

    // The runnable scan loop (WP7 slice 2e): the PaperTrading DbContext + persistence, the Scanning + PaperTrading
    // modules, and the configured read-only market-data feed driven by a background hosted service.
    builder.Services.AddScanLoop(builder.Configuration);

    // MarketData chart read-model (plan §9.1): the singleton ChartCandleStore the candle projection handler appends
    // to and the chart-candles query handler reads. Read-only projection of CandleIngested — it feeds the
    // dashboard's ICT Pattern Chart with real bars and routes nowhere near an order path (§6.3). (The Scanning
    // recent-setup overlay store is registered inside AddScanLoop → AddScanningModule.)
    builder.Services.AddMarketDataReadModels();

    // Performance module (WP6 / plan §5.3): the singleton PerformanceState the closed-trade handler appends to and
    // the summary + equity-curve query handlers read. Read-only R-based analytics — it consumes PaperTradeClosed.
    builder.Services.AddPerformanceModule();

    // Alerting module (plan §9): the singleton AlertLog the setup/trade alert handlers append to and the
    // recent-alerts query handler reads. Read-only advisory sink — it consumes SetupConfirmed + PaperTradeOpened/
    // Closed and publishes nothing, feeding the dashboard's Alerts feed.
    builder.Services.AddAlertingModule();

    // On-demand backtest engine (plan §15): an in-memory, deterministic run over recorded-history CSVs that REUSES
    // the pure §2.5 domain (scanner + orchestrator + account) — no bus, no DB. Pure analysis surface (§6.3).
    builder.Services.AddBacktesting(builder.Configuration);

    // Economic-calendar feed (plan §2.5.8/§15): the shared event store + the configured read-only source + a
    // background loader, so the §2.5.2 no-trade gate fires on real FOMC/NFP days. The scanner resolves the SAME
    // store singleton and loads it into each MarketContext. Disabled by default (the gate then stays fail-open).
    builder.Services.AddEconomicCalendar(builder.Configuration);
}

builder.Services.AddOpenApi();
builder.Services.AddSignalR();

var app = builder.Build();

// Single-origin deploy (plan §9): serve the built React dashboard (wwwroot) alongside the API + SignalR, so the whole
// app runs from ONE host/port with no dev proxy or CORS (the SPA calls /api + /hubs relative to its own origin). This
// is a no-op when wwwroot is absent (API-only dev mode); the SPA fallback below is mapped last so /api/* + /hubs/* win.
app.UseDefaultFiles();
app.UseStaticFiles();

// Resolving the options eagerly triggers ValidateOnStart (fail fast), then we announce the posture.
var defensive = app.Services.GetRequiredService<IOptions<DefensiveOptions>>().Value;
app.Logger.LogInformation(
    "DEFENSIVE MODE: analysis + paper only (LiveTradingEnabled={LiveTradingEnabled}).",
    defensive.LiveTradingEnabled);

app.MapOpenApi();

// Fetch-history mode is a one-shot, READ-ONLY CSV exporter (the OANDA history fetcher runs on startup, writes the
// CSVs, then stops the app) — it needs NO API surface. Boot it and exit BEFORE mapping the REST/SignalR endpoints:
// many of those endpoints depend on services that fetch mode deliberately does not register (the backtest engine,
// the runtime-settings store, the calendar store/options), so mapping them here would make minimal-API try to bind
// a missing service from the request body and crash startup ("Body was inferred…"). Guarding the map keeps fetch
// mode a clean standalone tool.
if (fetchHistoryMode)
{
    app.Run();
    return;
}

// Optional startup schema migration — a containerized / first-run convenience (plan §7). OFF by default so
// CI, the test suites, and local dev keep applying migrations explicitly (`dotnet ef database update`); the
// docker-compose `app` service sets Ict__Database__AutoMigrate=true so a fresh Postgres gets the schema on
// boot with no manual step. Both write-model contexts share the one database. Schema only — no order path (§6.3).
if (app.Configuration.GetValue<bool>("Ict:Database:AutoMigrate"))
{
    using var migrateScope = app.Services.CreateScope();
    migrateScope.ServiceProvider
        .GetRequiredService<IctTrader.PaperTrading.Infrastructure.Persistence.PaperTradingDbContext>()
        .Database.Migrate();
    migrateScope.ServiceProvider
        .GetRequiredService<IctTrader.MarketData.Infrastructure.Persistence.MarketDataDbContext>()
        .Database.Migrate();
    app.Logger.LogInformation("AutoMigrate: applied PaperTrading + MarketData schema migrations.");
}

// Frozen REST surface (plan §11.1 #6). WP0 returns typed empty results so the OpenAPI document carries
// the DTO shapes for the dashboard's generated types; real data is wired in WP3–WP7.
var api = app.MapGroup("/api");

// Real advisory alerts feed (plan §9): REST → bus QueryAsync → the Alerting module's
// GetRecentAlertsQueryHandler, which returns the most-recent setup/trade notifications newest-first from the
// bounded AlertLog. Read-only sink — surfacing an advisory notification routes nowhere near an order path (§6.3).
api.MapGet("/alerts", async (IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetRecentAlertsQuery(50))))
    .WithName("GetAlerts");

// The ranked "best opportunities" signals feed (plan §9 — "the system suggests the best setup"): REST → bus
// QueryAsync → the Scanning module's GetSignalsQueryHandler, which ranks the confirmed advisory setups across the
// whole (symbol × timeframe × style) matrix by grade → score → reward-to-risk → timeframe → recency and returns the
// filtered top-N. Optional filters: ?symbol= / ?style= (exact wire-name match), ?grade= (a floor — A or B), ?max=.
// Read-only/advisory — surfacing a ranked setup routes nowhere near an order path (§6.3 guardrail).
api.MapGet("/signals", async (string? symbol, string? style, string? grade, int? max, string? model, IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetSignalsQuery(symbol, style, grade, max, model))))
    .WithName("GetSignals");

// The operator TAKEs a Manual-mode pending signal (plan §15 — "give me the opportunity to use that setup"): REST →
// bus SendAsync → the PaperTrading TakeSetupCommandHandler, which opens ONE SIMULATED paper trade through the SAME
// shared SetupTradeOpener the automatic flow uses (so a taken trade is byte-identical in sizing/cap/guardrail). On
// success it echoes the opened PaperTradeDto (read back by the deterministic id); an unknown id → 404, an
// expired or already-taken id → 409. PAPER-ONLY — there is no broker/order path anywhere (§6.3 guardrail).
api.MapPost("/signals/{setupId:guid}/take", async (Guid setupId, IMessageBus bus) =>
    {
        try
        {
            await bus.SendAsync(new TakeSetupCommand(setupId));
            var trade = await bus.QueryAsync(new GetPaperTradeQuery(setupId));
            // The take opened/armed it; the trade is readable once it OPENED (Immediate) — Armed rests with no trade
            // row yet, so a null read on success means "armed", reported as 202 Accepted (queued, not yet a trade).
            return trade is null
                ? Results.Accepted($"/api/trades/{setupId}")
                : Results.Ok(trade);
        }
        catch (TakeSetupException ex)
        {
            // AlreadyTaken / Expired are CONFLICTs (the opportunity existed but can't be taken now — already opened, or
            // its entry window closed); a truly unknown id is a 404. The reason is echoed so the UI can tell them apart.
            return ex.Reason is TakeSetupFailure.AlreadyTaken or TakeSetupFailure.Expired
                ? Results.Conflict(new { error = ex.Message, reason = ex.Reason.ToString() })
                : Results.NotFound(new { error = ex.Message, reason = ex.Reason.ToString() });
        }
    })
    .WithName("TakeSignal");

// Real active-trades read-side (plan §4.1): REST → bus QueryAsync → the PaperTrading module's
// GetActiveTradesQueryHandler, which projects every OPEN PaperTrade aggregate to its wire DTO. Advisory only —
// the DTO carries no order field and routes nowhere (§6.3 guardrail).
api.MapGet("/trades/active", async (IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetActiveTradesQuery())))
    .WithName("GetActiveTrades");

// The full trades ledger (plan §5.3) — every trade, or filtered by status (Open/Closed) and/or symbol — for the
// dashboard's trades table: REST → bus QueryAsync → the PaperTrading module's GetTradesQueryHandler, which projects
// each PaperTrade aggregate (incl. its close reason, gross/net P&L, costs and R) to the wire DTO. Advisory only —
// the DTO carries no order field and routes nowhere (§6.3 guardrail).
api.MapGet("/trades", async (string? status, string? symbol, IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetTradesQuery(status, symbol))))
    .WithName("GetTrades");

// The live demo-account status (plan §5.1/§5.3) — equity vs starting equity, the adaptive-risk peak/trough + win/
// loss streaks, and open risk vs the §2.5.10 portfolio cap — for the dashboard's live-config panel: REST → bus
// QueryAsync → the PaperTrading module's GetAccountStatusQueryHandler. Read-only — routes nowhere near an order path.
api.MapGet("/account", async (IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetAccountStatusQuery())))
    .WithName("GetAccountStatus");

// Real R-based performance read-side (plan §5.3): REST → bus QueryAsync → the Performance module's
// GetPerformanceSummaryQueryHandler, which folds the accumulated closed-trade R stream through the pure
// PerformanceCalculator. Read-only analytics — it routes nowhere near an order path (§6.3 guardrail).
api.MapGet("/performance", async (string? model, IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetPerformanceSummaryQuery(model))))
    .WithName("GetPerformance");

// The per-model breakdown (plan §16): one §5.3 summary row per setup model that has closed trades, so the
// operator compares models side-by-side ("which setup performs best") next to the global headline aggregate.
api.MapGet("/performance/models", async (IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetModelPerformanceQuery())))
    .WithName("GetModelPerformance");

// The cumulative-R equity curve (plan §5.3) over the same closed-trade stream — REST → bus → the Performance
// module's GetEquityCurveQueryHandler. Each point is the running sum of R at a trade's close, ordered by time.
api.MapGet("/equity", async (string? model, IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetEquityCurveQuery(model))))
    .WithName("GetEquityCurve");

// Real ICT Pattern Chart read-side (plan §9.1 + plan §7 time-range extension):
//
// Without ?from / ?to  → ring buffer (ChartCandlesQuery) → CSV history fallback (unchanged behaviour).
// With    ?from / ?to  → DB range (GetChartRangeQuery) → CSV history fallback when persistence is off or
//                        the range returns no rows (e.g. candles not yet persisted).
//
// Both paths return the same ChartResponse shape + setup overlays — the wire is compatible. Read-only
// projections; routes nowhere near an order path (plan §6.3 guardrail).
api.MapGet("/chart/{symbol}", async (
        string symbol,
        string? tf,
        string? style,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? model,
        IMessageBus bus,
        BacktestEngine history) =>
    {
        var timeframe = tf ?? ChartDefaults.Timeframe;

        IReadOnlyList<CandleDto> candles;

        if (from.HasValue && to.HasValue)
        {
            // Time-range path: serve historical candles from Postgres (plan §7).
            candles = await bus.QueryAsync(
                new GetChartRangeQuery(symbol, timeframe, from.Value, to.Value));

            if (candles.Count == 0)
            {
                // Persistence disabled or no rows yet — fall back to CSV history tail for this symbol/TF.
                candles = ChartHistory.RecentCandles(history, symbol, timeframe, ChartDefaults.MaxCandles);
            }
        }
        else
        {
            // Recent-candles path: in-memory ring buffer (unchanged behaviour).
            candles = await bus.QueryAsync(
                new GetChartCandlesQuery(symbol, timeframe, ChartDefaults.MaxCandles));

            if (candles.Count == 0)
            {
                // No LIVE feed for this (symbol, timeframe) — serve the recorded CSV history so the chart
                // renders for ANY asset/timeframe the operator selects.
                candles = ChartHistory.RecentCandles(history, symbol, timeframe, ChartDefaults.MaxCandles);
            }
        }

        var overlays = await bus.QueryAsync(
            new GetRecentSetupsQuery(symbol, ChartDefaults.MaxOverlays));
        // The live "engine view" geometry for this (symbol, timeframe) — the concepts the scanner is tracking right
        // now, so the chart's concept toggles have data even between the rare confirmed setups (plan §9.1). Read-only.
        var geometryOverlays = await bus.QueryAsync(
            new GetGeometryOverlaysQuery(symbol, timeframe, ChartDefaults.MaxGeometryOverlays, model));
        return TypedResults.Ok(new ChartResponse(
            symbol, timeframe, style ?? ChartDefaults.Style, candles, overlays, geometryOverlays));
    })
    .WithName("GetChart");

// Market-session status (plan §2.1/§4.8): whether the FX market is open now, the current ICT killzone/session, and
// the next active killzone open (name + minutes) — all DST-aware NY time via NyClock. Read-only session math; the
// Host projects it from TimeProvider + the resolved active killzones. Routes nowhere near an order path (§6.3).
api.MapGet("/market-status", (TimeProvider timeProvider, IOptions<KillzoneEntryOptions> killzones) =>
        TypedResults.Ok(MarketStatus.Compute(timeProvider, killzones.Value.ResolvedActiveKillzones)))
    .WithName("GetMarketStatus");

// The live operator config (plan §4.6) — the RESOLVED Ict:* options the scanner is running under (provider,
// scanned symbols, active styles + killzones, base + portfolio risk, spread + commission, starting equity) — for
// the dashboard's live-config panel. The Host projects it directly from the injected options (it is the integrator
// that can read every module's config); read-only, routes nowhere near an order path (§6.3 guardrail).
api.MapGet("/config", (
        IOptions<MarketContextOptions> scanning,
        IOptions<KillzoneEntryOptions> killzones,
        IOptions<RiskOptions> risk,
        IOptions<ExecutionCostOptions> execution,
        IOptions<PaperTradingOptions> paperTrading,
        IConfiguration configuration) =>
    {
        var providerRaw = configuration.GetValue<string>("Ict:MarketData:Provider");
        var provider = string.IsNullOrWhiteSpace(providerRaw) ? "Replay" : providerRaw;
        return TypedResults.Ok(new ConfigStatusDto(
            Provider: provider,
            Symbols: ConfigStatusBuilder.ResolveSymbols(provider, configuration),
            ActiveStyles: scanning.Value.ResolvedActiveStyles.Select(s => s.ToString()).ToArray(),
            ActiveKillzones: killzones.Value.ResolvedActiveKillzones.Select(k => k.ToString()).ToArray(),
            BaseRiskPercent: risk.Value.BaseRiskPercent,
            MaxOpenPortfolioRiskPercent: risk.Value.MaxOpenPortfolioRiskPercent,
            SpreadBasePips: execution.Value.Spread.BasePips,
            CommissionPerLotRoundTripUsd: execution.Value.Commission.PerLotRoundTripUsd,
            StartingEquity: paperTrading.Value.StartingEquity));
    })
    .WithName("GetConfig");

// Live operator settings (plan §15): the per-instrument overrides (the editable tuning — k-of-n + required subset +
// per-symbol costs) the operator changes at RUNTIME. A change bumps the runtime revision so the scanner/orchestrator
// caches rebuild with the new options on the next candle — applied WITHOUT a restart. Read-only/advisory.
api.MapGet("/settings", (
        IRuntimeSettings settings,
        IOptions<ConfluenceOptions> confluence,
        IOptions<RiskOptions> risk,
        IOptions<ExecutionCostOptions> execution,
        IOptions<KillzoneEntryOptions> killzones,
        IOptions<MarketContextOptions> scanning,
        SetupModelCatalog models) =>
    {
        var c = confluence.Value;
        var r = risk.Value;
        var global = new GlobalConceptSettingsDto(
            RequiredConditions: c.EffectiveRequiredConditions.Select(x => x.ToString()).ToArray(),
            MinRequiredConditions: c.MinRequiredConditions,
            Weights: c.Weights.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            GradeAThreshold: c.GradeAThreshold,
            GradeBThreshold: c.GradeBThreshold,
            GradeCThreshold: c.GradeCThreshold,
            AlertMinimumGrade: c.AlertMinimumGrade.ToString(),
            BaseRiskPercent: r.BaseRiskPercent,
            MaxOpenPortfolioRiskPercent: r.MaxOpenPortfolioRiskPercent,
            HardMaxRiskPercent: r.HardMaxRiskPercent,
            MinStopDistancePips: r.MinStopDistancePips,
            LossLadderPercents: r.ResolvedLossLadderPercents,
            ConsecutiveWinsForLowestUnit: r.ConsecutiveWinsForLowestUnit,
            DipRecoveryFraction: r.DipRecoveryFraction,
            SpreadBasePips: execution.Value.Spread.BasePips,
            CommissionPerLotRoundTripUsd: execution.Value.Commission.PerLotRoundTripUsd,
            ActiveKillzones: killzones.Value.ResolvedActiveKillzones.Select(k => k.ToString()).ToArray(),
            ActiveStyles: scanning.Value.ResolvedActiveStyles.Select(s => s.ToString()).ToArray());

        return TypedResults.Ok(new SettingsDto(
            InstrumentOverrides: settings.InstrumentOverrides.ToDictionary(kv => kv.Key, kv => InstrumentSettingsDto.From(kv.Value)),
            Global: global,
            AvailableRequiredConditions: ConfluenceOptions.DefaultRequiredConditions.Select(x => x.ToString()).ToArray(),
            AvailableInstruments: InstrumentCatalog.KnownSymbols,
            // The LIVE selection: the operator's runtime override when set, else the configured default (plan §16).
            ActiveModels: (settings.ActiveModelsOverride ?? scanning.Value.ResolvedActiveModels)
                .Select(m => m.ToString()).ToArray(),
            AvailableModels: models.All.Select(d => d.Id.ToString()).ToArray()));
    })
    .WithName("GetSettings");

// The LIVE multi-select of active setup models (plan §16): which models the scanner runs, applied without a restart
// via the revision-stamped runtime-settings seam (the scanner caches rebuild on the next candle). Null/empty clears
// the override back to the configured default; an unknown model name is a 400. Read-only/advisory scanning scope —
// selecting models routes nowhere near an order path (§6.3).
api.MapPut("/settings/scanning", (ScanningSettingsUpdateDto? body, IRuntimeSettings settings, SetupModelCatalog models) =>
    {
        if (body?.ActiveModels is not { Count: > 0 })
        {
            settings.SetActiveModels(null);
            return Results.NoContent();
        }

        var parsed = new List<SetupModel>(body.ActiveModels.Count);
        foreach (var name in body.ActiveModels)
        {
            if (!Enum.TryParse<SetupModel>(name, ignoreCase: true, out var model) || !Enum.IsDefined(model))
            {
                return Results.BadRequest(new { error = $"Unknown setup model '{name}'." });
            }

            try
            {
                models.Resolve(model); // fail fast: a selectable model must have a shipped pipeline definition
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            parsed.Add(model);
        }

        settings.SetActiveModels(parsed);
        return Results.NoContent();
    })
    .WithName("PutScanningSettings");

// Set (or, with an empty body, clear) one symbol's live override. A clear reverts the symbol to the built-in catalog
// default. The override is validated (k-of-n range; a required subset must include DisplacementMss) before it applies.
api.MapPut("/settings/instruments/{symbol}", (string symbol, InstrumentSettingsDto? body, IRuntimeSettings settings) =>
    {
        try
        {
            if (body is null)
            {
                settings.SetInstrumentOverride(symbol, null);
                return Results.NoContent();
            }

            var overrides = body.ToOverrides();
            var errors = new InstrumentOverridesOptions { Overrides = { [symbol] = overrides } }.Validate();
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { error = string.Join("; ", errors) });
            }

            settings.SetInstrumentOverride(symbol, overrides);
            return Results.NoContent();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    })
    .WithName("PutInstrumentSettings");

// The economic-calendar status (plan §2.5.8/§15): whether the feed is enabled + loaded, the source provider, and the
// scheduled FOMC/NFP/CPI events in the NY-date window — each flagged if its date is a §2.5.2 no-trade day under the
// current blackout policy, plus the full set of blackout dates so the dashboard can mark the no-trade days. The Host
// projects it from the shared store + the gate options; read-only, routes nowhere near an order path (§6.3 guardrail).
api.MapGet("/calendar", (
        IEconomicCalendarStore store,
        IOptions<CalendarFeedOptions> feed,
        IOptions<CalendarOptions> gate,
        TimeProvider timeProvider) =>
    {
        var nyClock = new NyClock(timeProvider);
        var today = nyClock.NewYorkDate(nyClock.UtcNow);
        var from = today.AddDays(-feed.Value.LookbackDays);
        var to = today.AddDays(feed.Value.LookaheadDays);

        var events = store.Events
            .OrderBy(e => e.NyDate)
            .ThenBy(e => e.Type)
            .Select(e => new CalendarEventDto(
                Date: e.NyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Type: e.Type.ToString(),
                IsBlackout: CalendarBlackoutPolicy.IsBlackedOut(e.NyDate, store.Events, gate.Value)))
            .ToArray();

        // The no-trade days across the window (so the UI marks them even on dates with no event of their own — e.g.
        // the day AFTER an FOMC, or the Wednesday/Thursday BEFORE an NFP).
        var blackoutDates = new List<string>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (CalendarBlackoutPolicy.IsBlackedOut(d, store.Events, gate.Value))
            {
                blackoutDates.Add(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
        }

        return TypedResults.Ok(new CalendarStatusDto(
            Enabled: feed.Value.Enabled,
            Loaded: store.IsLoaded,
            Provider: feed.Value.Provider.ToString(),
            FromDate: from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ToDate: to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Events: events,
            BlackoutDates: blackoutDates));
    })
    .WithName("GetCalendar");

// The recorded-history datasets available to backtest (plan §15) — one per <symbol>-<tf>.csv with its date range +
// candle count, so the Backtest Lab can bound its period picker. Read-only directory scan.
api.MapGet("/backtest/datasets", (BacktestEngine engine) =>
        TypedResults.Ok(engine.ListDatasets()))
    .WithName("GetBacktestDatasets");

// Run an on-demand backtest (plan §15): the in-memory engine replays the chosen symbol/timeframe/period through the
// pure §2.5 domain with the requested style + starting balance + risk, and returns the summary, equity curve, and
// every trade. Synchronous CPU work is pushed off the request thread; a bad request → 400, a missing dataset → 404.
// Advisory only — it reads a CSV and routes nothing near a broker (§6.3 guardrail).
api.MapPost("/backtest", async (BacktestRequest request, BacktestEngine engine) =>
    {
        try
        {
            var result = await Task.Run(() => engine.Run(request)).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    })
    .WithName("RunBacktest");

// Optimize (plan §15): sweep the backtest engine across symbols × styles × timeframes × risk percentages and return
// a ranked leaderboard, so an operator can find the optimum settings per asset/timeframe/style. Each combination is
// an isolated in-memory run; datasets are cached per (symbol, timeframe). A bad/oversized grid → 400. Advisory only.
api.MapPost("/backtest/optimize", async (OptimizeRequest request, BacktestOptimizer optimizer, CancellationToken ct) =>
    {
        try
        {
            return Results.Ok(await optimizer.OptimizeAsync(request, ct).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    })
    .WithName("OptimizeBacktest");

// Advisory only — this NEVER routes to a broker (plan §6.3); the simulator is wired in WP4.
api.MapPost("/paper-trades", (ExecutePaperTradeRequest request) =>
        TypedResults.Accepted($"/api/trades/{request.SetupId}"))
    .WithName("CreatePaperTrade");

app.MapHub<TradingHub>(TradingHub.Route);

// SPA client-side routing: any non-API, non-hub, non-file request falls back to the dashboard's index.html so deep
// links (/trades, /backtest, /settings, …) resolve. Mapped LAST, so the /api/* + /hubs/* endpoints take precedence;
// a no-op (404) when wwwroot/index.html is absent (API-only mode).
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so the integration/E2E suites can host the app via WebApplicationFactory (plan §8.2).</summary>
public partial class Program
{
}
