using IctTrader.Host.Hubs;
using IctTrader.MarketData.Contracts;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Performance.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.AspNetCore.SignalR;

namespace IctTrader.Host.Realtime;

/// <summary>
/// The bus -> SignalR transport bridge (plan §9 / WP7). These Host-resident handlers subscribe to the
/// in-memory bus's integration events and push them to every connected dashboard client through the
/// push-only <see cref="TradingHub"/>, so the OHLC chart, alerts feed, active-trades table, and performance
/// panel update live as the scan loop runs. They live in the Host because the hub does — and they ORCHESTRATE
/// transport only: each is a thin <c>Clients.All.SendAsync</c> with NO domain logic (the domain DECIDED long
/// before the event was published).
///
/// <para><b>Guardrail (plan §6.3):</b> the bridge is one-directional — bus -> hub -> browser. The hub stays
/// push-only (no client-callable trading method), and every payload is an advisory, read-only DTO that routes
/// nowhere near an order path.</para>
///
/// <para><b>Resilience:</b> the bus delivers an event to ALL registered handlers sequentially within one
/// dispatch scope, so a broadcaster MUST never throw and break the chain — a dashboard push failure must not
/// abort candle ingestion or trade settlement. Each <c>HandleAsync</c> therefore logs-and-swallows any push
/// exception. With no clients connected (e.g. during a backtest), <c>Clients.All.SendAsync</c> is a harmless
/// no-op.</para>
/// </summary>
internal static class HubBroadcast
{
    /// <summary>
    /// Pushes <paramref name="payload"/> to every connected client under <paramref name="clientMethod"/>,
    /// logging and swallowing any transport failure so the bus dispatch chain is never broken.
    /// </summary>
    public static async Task SendSafelyAsync<TPayload>(
        IHubContext<TradingHub> hub,
        ILogger logger,
        string clientMethod,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.All.SendAsync(clientMethod, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A live-push failure is advisory-only: log it and let the bus dispatch chain continue.
            logger.LogWarning(
                ex, "Live SignalR push of {ClientMethod} to dashboard clients failed; continuing.", clientMethod);
        }
    }
}

/// <summary>Bridges each ingested candle to the dashboard's ICT Pattern Chart (<see cref="TradingHub.CandleAppended"/>).</summary>
internal sealed class CandleIngestedBroadcaster(
    IHubContext<TradingHub> hub, ILogger<CandleIngestedBroadcaster> logger)
    : IEventHandler<CandleIngested>
{
    private readonly IHubContext<TradingHub> _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    private readonly ILogger<CandleIngestedBroadcaster> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task HandleAsync(CandleIngested @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return HubBroadcast.SendSafelyAsync(_hub, _logger, TradingHub.CandleAppended, @event.Candle, cancellationToken);
    }
}

/// <summary>Bridges each confirmed advisory setup to the dashboard's alerts feed (<see cref="TradingHub.SetupDetected"/>).</summary>
internal sealed class SetupConfirmedBroadcaster(
    IHubContext<TradingHub> hub, ILogger<SetupConfirmedBroadcaster> logger)
    : IEventHandler<SetupConfirmed>
{
    private readonly IHubContext<TradingHub> _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    private readonly ILogger<SetupConfirmedBroadcaster> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task HandleAsync(SetupConfirmed @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return HubBroadcast.SendSafelyAsync(_hub, _logger, TradingHub.SetupDetected, @event.Setup, cancellationToken);
    }
}

/// <summary>Bridges each opened paper trade to the dashboard's active-trades table (<see cref="TradingHub.TradeUpdated"/>).</summary>
internal sealed class PaperTradeOpenedBroadcaster(
    IHubContext<TradingHub> hub, ILogger<PaperTradeOpenedBroadcaster> logger)
    : IEventHandler<PaperTradeOpened>
{
    private readonly IHubContext<TradingHub> _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    private readonly ILogger<PaperTradeOpenedBroadcaster> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task HandleAsync(PaperTradeOpened @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return HubBroadcast.SendSafelyAsync(_hub, _logger, TradingHub.TradeUpdated, @event.Trade, cancellationToken);
    }
}

/// <summary>Bridges each closed paper trade to the dashboard's active-trades table (<see cref="TradingHub.TradeUpdated"/>).</summary>
internal sealed class PaperTradeClosedBroadcaster(
    IHubContext<TradingHub> hub, ILogger<PaperTradeClosedBroadcaster> logger)
    : IEventHandler<PaperTradeClosed>
{
    private readonly IHubContext<TradingHub> _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    private readonly ILogger<PaperTradeClosedBroadcaster> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task HandleAsync(PaperTradeClosed @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return HubBroadcast.SendSafelyAsync(_hub, _logger, TradingHub.TradeUpdated, @event.Trade, cancellationToken);
    }
}

/// <summary>Bridges each recomputed performance summary to the dashboard's performance panel (<see cref="TradingHub.PerformanceUpdated"/>).</summary>
internal sealed class PerformanceUpdatedBroadcaster(
    IHubContext<TradingHub> hub, ILogger<PerformanceUpdatedBroadcaster> logger)
    : IEventHandler<PerformanceUpdated>
{
    private readonly IHubContext<TradingHub> _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    private readonly ILogger<PerformanceUpdatedBroadcaster> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task HandleAsync(PerformanceUpdated @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return HubBroadcast.SendSafelyAsync(_hub, _logger, TradingHub.PerformanceUpdated, @event.Summary, cancellationToken);
    }
}
