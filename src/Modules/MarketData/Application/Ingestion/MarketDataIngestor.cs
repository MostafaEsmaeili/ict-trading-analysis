using IctTrader.MarketData.Application.Abstractions;
using IctTrader.MarketData.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IctTrader.MarketData.Application.Ingestion;

/// <summary>
/// Drives a <see cref="IMarketDataFeed"/> into the bus (plan §4.1): pulls candles in chronological order and
/// publishes one <see cref="CandleIngested"/> per candle, so downstream modules (Scanning, PaperTrading)
/// react in deterministic candle order. The feed is read-only by SHAPE (it exposes no write/order method —
/// plan §6.3), so ingestion can only ever read. This is the pure ingestion logic; the hosted
/// <c>BackgroundService</c> that calls <see cref="IngestAsync"/> is wired in the Host (WP7 slice 2e).
///
/// <para><b>Per-candle resilience.</b> A single recoverable bar — a data-shape fault (e.g. an unparseable
/// timeframe a downstream handler rejects) or a transient downstream error (a DB/xmin conflict on a handler's
/// commit) — must NOT abort a long-running backtest or live stream. Each <see cref="CandleIngested"/> publish is
/// therefore isolated: the offending bar is logged and skipped, and ingestion continues with the next bar. A
/// genuine cooperative cancellation (a real shutdown) is the one exception that is rethrown so the stream tears
/// down. This mirrors the OANDA live-poll feed's own transient-error isolation; it does not replace the hosted
/// service's last-resort boundary.</para>
/// </summary>
public sealed class MarketDataIngestor(IMarketDataFeed feed, IMessageBus bus, ILogger<MarketDataIngestor>? logger = null)
{
    private readonly IMarketDataFeed _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly ILogger<MarketDataIngestor> _logger = logger ?? NullLogger<MarketDataIngestor>.Instance;

    public async Task IngestAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var candle in _feed.StreamCandlesAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await _bus.PublishAsync(new CandleIngested(candle), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // a real shutdown must still tear the stream down
            }
            catch (Exception ex)
            {
                // One recoverable bar (a data-shape error like an unparseable timeframe, or a transient DB/xmin
                // conflict in a downstream handler) must NOT kill the whole loop — log the offending candle and
                // continue to the next bar (mirrors the OANDA live-poll transient-error isolation).
                _logger.LogWarning(
                    ex,
                    "Skipping candle {Symbol} {Timeframe} @ {OpenTimeUtc:o}; ingestion continues.",
                    candle.Symbol,
                    candle.Timeframe,
                    candle.OpenTimeUtc);
            }
        }
    }
}
