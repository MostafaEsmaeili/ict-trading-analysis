using System.Threading.Channels;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IctTrader.MarketData.Application.Persistence;

/// <summary>
/// Drains the bounded <see cref="Channel{T}"/> filled by <see cref="CandlePersistenceProjectionHandler"/>
/// and flushes candles to Postgres in BATCHES (plan §7 — "batched dual-write").
/// <para>
/// <b>Batching:</b> the service waits up to <c>FlushIntervalMs</c> OR until the channel delivers
/// <c>BatchSize</c> candles, then calls <see cref="ICandleRepository.AppendAsync"/> in its OWN DI scope
/// (so the scoped <c>MarketDataDbContext</c> is isolated from the scan dispatch's scope).
/// </para>
/// <para>
/// <b>Resilience:</b> a DB outage logs an error and continues — the scan dispatch is NEVER blocked or
/// failed.  The in-memory ring buffer remains the authoritative live tail; the DB is the durable
/// historical archive.
/// </para>
/// <para>
/// <b>Read-model only (plan §6.3 guardrail):</b> flushing OHLC bars to the <c>candles</c> table routes
/// nowhere near an order path.
/// </para>
/// </summary>
public sealed class CandlePersistenceHostedService(
    Channel<Candle> channel,
    IServiceScopeFactory scopeFactory,
    CandlePersistenceBatchOptions batchOptions,
    ILogger<CandlePersistenceHostedService> logger)
    : BackgroundService
{
    private readonly Channel<Candle> _channel =
        channel ?? throw new ArgumentNullException(nameof(channel));

    private readonly IServiceScopeFactory _scopeFactory =
        scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    private readonly CandlePersistenceBatchOptions _opts =
        batchOptions ?? throw new ArgumentNullException(nameof(batchOptions));

    private readonly ILogger<CandlePersistenceHostedService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CandlePersistenceHostedService started (BatchSize={BatchSize}, FlushIntervalMs={FlushIntervalMs}).",
            _opts.BatchSize, _opts.FlushIntervalMs);

        var batch = new List<Candle>(_opts.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            batch.Clear();

            // Wait for the first candle (or cancellation).
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
                    break; // channel completed
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Drain up to BatchSize candles within FlushIntervalMs.
            using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            flushCts.CancelAfter(_opts.FlushIntervalMs);

            try
            {
                while (batch.Count < _opts.BatchSize &&
                       _channel.Reader.TryRead(out var candle))
                {
                    batch.Add(candle);
                }

                // If we didn't fill the batch immediately, keep reading until the flush timer fires.
                while (batch.Count < _opts.BatchSize)
                {
                    try
                    {
                        if (!await _channel.Reader.WaitToReadAsync(flushCts.Token).ConfigureAwait(false))
                            break;
                    }
                    catch (OperationCanceledException)
                    {
                        // Flush timer expired OR host stopping — flush whatever we have.
                        break;
                    }

                    while (batch.Count < _opts.BatchSize && _channel.Reader.TryRead(out var extra))
                    {
                        batch.Add(extra);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down — flush the current partial batch then stop.
            }

            if (batch.Count == 0)
                continue;

            await FlushAsync(batch, stoppingToken).ConfigureAwait(false);
        }

        // Drain the remainder of the channel on graceful shutdown.
        _logger.LogInformation(
            "CandlePersistenceHostedService stopping; draining remaining candles from channel.");

        while (_channel.Reader.TryRead(out var c))
            batch.Add(c);

        if (batch.Count > 0)
            await FlushAsync(batch, CancellationToken.None).ConfigureAwait(false);

        _logger.LogInformation("CandlePersistenceHostedService stopped.");
    }

    private async Task FlushAsync(List<Candle> batch, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<ICandleRepository>();
            await repo.AppendAsync(batch, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "CandlePersistenceHostedService: flushed {Count} candles to Postgres.", batch.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A DB outage must NOT fail the scan dispatch. Log and continue — the ring buffer is unaffected.
            _logger.LogError(
                ex,
                "CandlePersistenceHostedService: failed to flush {Count} candles to Postgres. " +
                "The in-memory chart ring buffer is unaffected; these bars will be missing from " +
                "the persistent history. The writer will retry on the next batch.",
                batch.Count);
        }
    }
}

/// <summary>
/// Carries the batch-size and flush-interval from <c>CandlePersistenceOptions</c> into
/// <see cref="CandlePersistenceHostedService"/> without pulling the full
/// <c>IOptions&lt;CandlePersistenceOptions&gt;</c> infrastructure into the Application layer.
/// Populated by the Host DI wiring.
/// </summary>
public sealed class CandlePersistenceBatchOptions(int batchSize, int flushIntervalMs)
{
    public int BatchSize { get; } =
        batchSize > 0 ? batchSize : throw new ArgumentOutOfRangeException(nameof(batchSize));

    public int FlushIntervalMs { get; } =
        flushIntervalMs > 0 ? flushIntervalMs : throw new ArgumentOutOfRangeException(nameof(flushIntervalMs));
}
