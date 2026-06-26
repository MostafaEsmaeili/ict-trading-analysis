using IctTrader.MarketData.Application.Abstractions;
using IctTrader.MarketData.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.MarketData.Application.Ingestion;

/// <summary>
/// Drives a <see cref="IMarketDataFeed"/> into the bus (plan §4.1): pulls candles in chronological order and
/// publishes one <see cref="CandleIngested"/> per candle, so downstream modules (Scanning, PaperTrading)
/// react in deterministic candle order. This is the pure ingestion logic; the hosted
/// <c>BackgroundService</c> that calls <see cref="IngestAsync"/> is wired in the Host (WP7 slice 2e).
/// </summary>
public sealed class MarketDataIngestor(IMarketDataFeed feed, IMessageBus bus)
{
    private readonly IMarketDataFeed _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));

    public async Task IngestAsync(CancellationToken cancellationToken = default)
    {
        // Structural guardrail (plan §6.3): we only ever ingest from a read-only feed.
        if (!_feed.IsReadOnly)
        {
            throw new InvalidOperationException(
                $"Feed '{_feed.Provider}' is not read-only; ingestion is analysis-only and refuses a writable feed.");
        }

        await foreach (var candle in _feed.StreamCandlesAsync(cancellationToken).ConfigureAwait(false))
        {
            await _bus.PublishAsync(new CandleIngested(candle), cancellationToken).ConfigureAwait(false);
        }
    }
}
