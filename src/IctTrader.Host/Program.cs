using IctTrader.Alerting.Contracts;
using IctTrader.Host;
using IctTrader.Host.Hubs;
using IctTrader.MarketData.Contracts;
using IctTrader.PaperTrading.Contracts;
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

// In-memory message bus (plan §3.0a) — the only inter-module seam. The bus singleton is registered with the two
// module Application assemblies so their handlers (Scanning's CandleIngestedHandler, PaperTrading's
// SetupConfirmedHandler + candle handler) are Scrutor-scanned in, closing the candle→scan→paper-trade chain.
builder.Services.AddMessaging(
    typeof(IctTrader.Scanning.Application.Scanning.CandleIngestedHandler).Assembly,
    typeof(IctTrader.PaperTrading.Application.Trading.SetupConfirmedHandler).Assembly);

// The runnable scan loop (WP7 slice 2e): the PaperTrading DbContext + persistence, the Scanning + PaperTrading
// modules, and the read-only replay feed driven by a background hosted service.
builder.Services.AddScanLoop(builder.Configuration);

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

api.MapGet("/trades/active", () => TypedResults.Ok(Array.Empty<PaperTradeDto>()))
    .WithName("GetActiveTrades");

api.MapGet("/performance", () => TypedResults.Ok(new PerformanceSummaryDto(0, 0m, 0m, 0m, 0m, 0m)))
    .WithName("GetPerformance");

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
