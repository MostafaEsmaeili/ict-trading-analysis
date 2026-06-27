using IctTrader.MarketData.Application.Abstractions;
using IctTrader.MarketData.Application.Ingestion;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Host;

/// <summary>
/// Drives whatever <see cref="IMarketDataFeed"/> the operator selected (<c>Ict:MarketData:Provider</c>) into the
/// bus on startup (WP7): it resolves the registered feed — the Replay CSV-fixture feed OR the OANDA-practice feed —
/// builds a <see cref="MarketDataIngestor"/>, and streams every candle through it so the Scanning and PaperTrading
/// handlers run the full scan→paper-trade chain. ONE hosted service serves BOTH providers because both expose the
/// same read-only <see cref="IMarketDataFeed"/> shape (a candle stream, no order/broker path — the NON-NEGOTIABLE
/// guardrail is structural).
///
/// <para>The feed is resolved INSIDE <see cref="ExecuteAsync"/> within a try/catch, so a bad OANDA token, an
/// unreachable feed, a bad/missing replay fixture, or a not-yet-migrated DB is LOGGED rather than crashing host
/// startup — the operator gets a clear diagnostic and the REST/SignalR surface stays up. (Hard mis-configuration
/// — a blank OANDA token, a Replay-enabled feed with no connection string — still fails fast at startup via the
/// respective <c>ValidateOnStart</c> / <c>AddScanLoop</c> guards.)</para>
/// </summary>
internal sealed class MarketDataIngestionHostedService(
    IServiceProvider services,
    IMessageBus bus,
    ILogger<MarketDataIngestionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IMarketDataFeed feed;
        try
        {
            // Resolve the feed INSIDE the try: the Replay feed reads its CSV fixture here (a bad path throws), and
            // the OANDA typed client may surface a configuration problem — either is logged below, not fatal.
            feed = services.GetRequiredService<IMarketDataFeed>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Market-data feed could not be resolved; the API surface stays up, ingestion is idle.");
            return;
        }

        logger.LogInformation("Market-data ingestion starting on provider {Provider}.", feed.Provider);
        try
        {
            // Pass the real ingestor logger so a per-candle skip (one bad/transient bar) is observable in production.
            var ingestorLogger = services.GetRequiredService<ILogger<MarketDataIngestor>>();
            await new MarketDataIngestor(feed, bus, ingestorLogger).IngestAsync(stoppingToken).ConfigureAwait(false);
            logger.LogInformation("Market-data ingestion complete on provider {Provider}.", feed.Provider);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A normal shutdown during ingestion — not an error.
            logger.LogInformation("Market-data ingestion cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            // Intentional top-level boundary catch: an ingest failure (bad token, transient feed error on a finite
            // stream) must not crash the host — log + stay up.
            logger.LogError(ex, "Market-data ingestion failed on provider {Provider}; the API surface stays up.", feed.Provider);
        }
    }
}
