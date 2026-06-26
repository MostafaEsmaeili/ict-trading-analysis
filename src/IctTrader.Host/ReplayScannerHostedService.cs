using IctTrader.MarketData.Application.Ingestion;
using IctTrader.MarketData.Infrastructure.Feeds;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.Options;

namespace IctTrader.Host;

/// <summary>
/// Drives the replay feed once on startup (WP7 slice 2e): when replay is enabled it loads the configured CSV candle
/// fixture and streams it through a <see cref="MarketDataIngestor"/>, which publishes each candle onto the in-memory
/// bus so the Scanning and PaperTrading handlers run the full scan→paper-trade chain. When replay is disabled (the
/// default) it logs that the scanner is idle and returns — the Host runs as a pure REST/SignalR surface until a
/// fixture is wired. The feed is the ONLY market-data source and is read-only by shape (no order/broker path).
///
/// <para>The fixture is loaded HERE (not in a DI factory), so a bad/missing fixture path — or a DB that is not yet
/// migrated — is logged rather than crashing host startup; the operator gets a clear diagnostic and the API stays up.</para>
/// </summary>
internal sealed class ReplayScannerHostedService(
    IMessageBus bus,
    IOptions<ReplayFeedOptions> options,
    ILogger<ReplayScannerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var replay = options.Value;
        if (!replay.Enabled)
        {
            logger.LogInformation("Replay feed disabled, scanner idle (Ict:MarketData:Replay:Enabled=false).");
            return;
        }

        logger.LogInformation("Replay scanner starting: ingesting fixture {FixturePath}.", replay.FixturePath);
        try
        {
            // Load + ingest INSIDE the try (FixturePath is non-blank — ValidateOnStart guarantees it), so a bad or
            // missing fixture is caught + logged below rather than thrown during host startup.
            var feed = new ReplayMarketDataFeed(CsvCandleSource.Load(replay.FixturePath!));
            await new MarketDataIngestor(feed, bus).IngestAsync(stoppingToken).ConfigureAwait(false);
            logger.LogInformation("Replay scanner complete: fixture ingestion finished.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A normal shutdown during ingestion — not an error.
            logger.LogInformation("Replay scanner cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            // Intentional top-level boundary catch: a replay/ingest failure must not crash the host — log + stay up.
            logger.LogError(ex, "Replay scanner failed during fixture ingestion; the API surface stays up.");
        }
    }
}
