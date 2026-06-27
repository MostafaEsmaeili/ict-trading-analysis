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
/// <para><b>Per-candle resilience with a circuit breaker.</b> A single recoverable bar — a data-shape fault
/// (e.g. an unparseable timeframe a downstream handler rejects) or a transient downstream error (a DB/xmin
/// conflict on a handler's commit) — must NOT abort a long-running backtest or live stream. Each
/// <see cref="CandleIngested"/> publish is therefore isolated: the offending bar is logged and skipped, and
/// ingestion continues with the next bar. A genuine cooperative cancellation (a real shutdown) is rethrown so
/// the stream tears down. BUT a blanket log-and-continue would also demote a DETERMINISTIC handler bug (one that
/// throws on every candle) to repeated Warning noise while the run reports completion with zero trades. To keep
/// that bug surfacing, consecutive failures are counted and the stream is aborted once
/// <see cref="MaxConsecutivePublishFailures"/> is hit (the exception is rethrown, reaching the hosted service's
/// top-level Error boundary); the counter resets on any successful publish, so isolated transient blips are still
/// tolerated. This mirrors the OANDA live-poll feed's own transient-error isolation; it does not replace the
/// hosted service's last-resort boundary.</para>
/// </summary>
public sealed class MarketDataIngestor(IMarketDataFeed feed, IMessageBus bus, ILogger<MarketDataIngestor>? logger = null)
{
    /// <summary>
    /// Consecutive per-candle publish failures tolerated before the stream is aborted (rethrown to the hosted
    /// service's Error boundary). Generous enough to ride out a burst of transient downstream blips, low enough
    /// that a deterministic handler bug (throws on every candle) surfaces quickly instead of producing a
    /// silent, "successful" empty run. Any successful publish resets the count to zero.
    /// </summary>
    public const int MaxConsecutivePublishFailures = 50;

    private readonly IMarketDataFeed _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly ILogger<MarketDataIngestor> _logger = logger ?? NullLogger<MarketDataIngestor>.Instance;

    public async Task IngestAsync(CancellationToken cancellationToken = default)
    {
        var consecutiveFailures = 0;

        await foreach (var candle in _feed.StreamCandlesAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await _bus.PublishAsync(new CandleIngested(candle), cancellationToken).ConfigureAwait(false);
                consecutiveFailures = 0; // a clean publish clears the streak — isolated blips don't accumulate
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
                consecutiveFailures++;
                if (consecutiveFailures >= MaxConsecutivePublishFailures)
                {
                    // A run of failures this long is a deterministic bug, not a transient blip — abort the stream
                    // so it surfaces at the hosted service's Error boundary instead of an "empty but successful" run.
                    _logger.LogError(
                        ex,
                        "Aborting ingestion: {Count} consecutive candle publishes failed (last: {Symbol} {Timeframe} @ {OpenTimeUtc:o}).",
                        consecutiveFailures,
                        candle.Symbol,
                        candle.Timeframe,
                        candle.OpenTimeUtc);
                    throw;
                }

                _logger.LogWarning(
                    ex,
                    "Skipping candle {Symbol} {Timeframe} @ {OpenTimeUtc:o}; ingestion continues ({Count}/{Max} consecutive failures).",
                    candle.Symbol,
                    candle.Timeframe,
                    candle.OpenTimeUtc,
                    consecutiveFailures,
                    MaxConsecutivePublishFailures);
            }
        }
    }
}
