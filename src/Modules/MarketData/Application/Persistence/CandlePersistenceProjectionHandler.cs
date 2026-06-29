using System.Threading.Channels;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace IctTrader.MarketData.Application.Persistence;

/// <summary>
/// The candle-persistence write side of the dual-write pattern (plan §7):
/// reacts to each <see cref="CandleIngested"/> on the bus and enqueues the candle into a bounded
/// <see cref="Channel{T}"/> consumed by <see cref="CandlePersistenceHostedService"/>.
/// <para>
/// <b>Non-blocking (plan §7 "respect the documented per-candle-DB bottleneck"):</b> the enqueue is a
/// <c>TryWrite</c> (fire-and-forget) onto the bounded channel.  The scan dispatch is NEVER delayed by a
/// DB round-trip.  If the channel is full (the background writer can't keep up with the ingestion rate)
/// the handler logs a warning and drops the candle — the in-memory <see cref="Chart.ChartCandleStore"/>
/// ring buffer still holds the recent tail.
/// </para>
/// <para>
/// <b>Read-model only (plan §6.3 guardrail):</b> enqueuing an OHLC bar routes nowhere near an order path.
/// </para>
/// </summary>
public sealed class CandlePersistenceProjectionHandler(
    Channel<Candle> channel,
    ILogger<CandlePersistenceProjectionHandler> logger)
    : IEventHandler<CandleIngested>
{
    private readonly Channel<Candle> _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    private readonly ILogger<CandlePersistenceProjectionHandler> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public Task HandleAsync(CandleIngested @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var dto = @event.Candle;

        // Parse the Timeframe enum; an unknown string is silently dropped (the in-memory store already
        // holds it for the live chart window and the DB would reject an unknown value anyway).
        if (!Enum.TryParse<Timeframe>(dto.Timeframe, ignoreCase: false, out var tf))
        {
            _logger.LogWarning(
                "CandlePersistenceProjectionHandler: unknown timeframe '{Timeframe}' for {Symbol} — skipped.",
                dto.Timeframe, dto.Symbol);
            return Task.CompletedTask;
        }

        var candle = new Candle(
            new Symbol(dto.Symbol), tf, dto.OpenTimeUtc,
            dto.Open, dto.High, dto.Low, dto.Close, dto.Volume);

        if (!_channel.Writer.TryWrite(candle))
        {
            _logger.LogWarning(
                "CandlePersistenceProjectionHandler: channel is full; dropping {Symbol} {Timeframe} candle " +
                "at {OpenTime:O}. The background writer cannot keep up. " +
                "The in-memory chart ring buffer is unaffected.",
                dto.Symbol, dto.Timeframe, dto.OpenTimeUtc);
        }

        return Task.CompletedTask;
    }
}
