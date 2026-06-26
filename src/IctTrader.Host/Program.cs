using IctTrader.Alerting.Contracts;
using IctTrader.Host;
using IctTrader.Host.Hubs;
using IctTrader.MarketData.Contracts;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Performance.Application;
using IctTrader.Performance.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
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

// In-memory message bus (plan §3.0a) — the only inter-module seam. The bus singleton is registered with the
// module Application assemblies so their handlers are Scrutor-scanned in: Scanning's CandleIngestedHandler,
// PaperTrading's SetupConfirmedHandler + candle handler, and Performance's PaperTradeClosedHandler + query
// handlers — closing the candle→scan→paper-trade→performance chain.
builder.Services.AddMessaging(
    typeof(IctTrader.Scanning.Application.Scanning.CandleIngestedHandler).Assembly,
    typeof(IctTrader.PaperTrading.Application.Trading.SetupConfirmedHandler).Assembly,
    typeof(IctTrader.Performance.Application.PaperTradeClosedHandler).Assembly);

// The runnable scan loop (WP7 slice 2e): the PaperTrading DbContext + persistence, the Scanning + PaperTrading
// modules, and the read-only replay feed driven by a background hosted service.
builder.Services.AddScanLoop(builder.Configuration);

// Performance module (WP6 / plan §5.3): the singleton PerformanceState the closed-trade handler appends to and the
// summary + equity-curve query handlers read. Read-only R-based analytics — it consumes PaperTradeClosed only.
builder.Services.AddPerformanceModule();

builder.Services.AddOpenApi();
builder.Services.AddSignalR();

var app = builder.Build();

// Resolving the options eagerly triggers ValidateOnStart (fail fast), then we announce the posture.
var defensive = app.Services.GetRequiredService<IOptions<DefensiveOptions>>().Value;
app.Logger.LogInformation(
    "DEFENSIVE MODE: analysis + paper only (LiveTradingEnabled={LiveTradingEnabled}).",
    defensive.LiveTradingEnabled);

app.MapOpenApi();

// Frozen REST surface (plan §11.1 #6). WP0 returns typed empty results so the OpenAPI document carries
// the DTO shapes for the dashboard's generated types; real data is wired in WP3–WP7.
var api = app.MapGroup("/api");

api.MapGet("/alerts", () => TypedResults.Ok(Array.Empty<AlertDto>()))
    .WithName("GetAlerts");

// Real active-trades read-side (plan §4.1): REST → bus QueryAsync → the PaperTrading module's
// GetActiveTradesQueryHandler, which projects every OPEN PaperTrade aggregate to its wire DTO. Advisory only —
// the DTO carries no order field and routes nowhere (§6.3 guardrail).
api.MapGet("/trades/active", async (IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetActiveTradesQuery())))
    .WithName("GetActiveTrades");

// Real R-based performance read-side (plan §5.3): REST → bus QueryAsync → the Performance module's
// GetPerformanceSummaryQueryHandler, which folds the accumulated closed-trade R stream through the pure
// PerformanceCalculator. Read-only analytics — it routes nowhere near an order path (§6.3 guardrail).
api.MapGet("/performance", async (IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetPerformanceSummaryQuery())))
    .WithName("GetPerformance");

// The cumulative-R equity curve (plan §5.3) over the same closed-trade stream — REST → bus → the Performance
// module's GetEquityCurveQueryHandler. Each point is the running sum of R at a trade's close, ordered by time.
api.MapGet("/equity", async (IMessageBus bus) =>
        TypedResults.Ok(await bus.QueryAsync(new GetEquityCurveQuery())))
    .WithName("GetEquityCurve");

api.MapGet("/chart/{symbol}", (string symbol, string? tf, string? style) =>
        TypedResults.Ok(new ChartResponse(
            symbol,
            tf ?? "M5",
            style ?? "Intraday",
            Array.Empty<CandleDto>(),
            Array.Empty<SetupDto>())))
    .WithName("GetChart");

// Advisory only — this NEVER routes to a broker (plan §6.3); the simulator is wired in WP4.
api.MapPost("/paper-trades", (ExecutePaperTradeRequest request) =>
        TypedResults.Accepted($"/api/trades/{request.SetupId}"))
    .WithName("CreatePaperTrade");

app.MapHub<TradingHub>(TradingHub.Route);

app.Run();

/// <summary>Exposed so the integration/E2E suites can host the app via WebApplicationFactory (plan §8.2).</summary>
public partial class Program
{
}
