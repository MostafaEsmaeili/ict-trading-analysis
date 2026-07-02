using IctTrader.Alerting.Contracts;
using IctTrader.PaperTrading.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Alerting.Application;

/// <summary>
/// The Alerting module's trade-closed sink (plan §3.0a / §9): it reacts to each <see cref="PaperTradeClosed"/>
/// from the PaperTrading module and projects the closed SIMULATED trade — with its outcome and realized R — into
/// a concise <see cref="AlertDto"/> appended to the singleton <see cref="AlertLog"/>, so the dashboard's Alerts
/// feed shows how each paper position resolved. The handler MAPS only.
///
/// <para>A settled trade always carries a close time; the null-coalescing to the injected clock's UTC now is purely
/// defensive (never <c>DateTime.Now</c> — the ambient process zone is forbidden, plan §4.8). A null R is treated as
/// a flat scratch (0R).</para>
///
/// <para>Read-only sink (plan §6.3 guardrail): the trade is a SIMULATED paper trade and surfacing its close as a
/// notification routes nowhere near an order path.</para>
/// </summary>
public sealed class PaperTradeClosedAlertHandler(AlertLog log, TimeProvider timeProvider)
    : IEventHandler<PaperTradeClosed>
{
    private readonly AlertLog _log = log ?? throw new ArgumentNullException(nameof(log));

    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public Task HandleAsync(PaperTradeClosed @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var trade = @event.Trade;
        var realizedR = trade.RealizedR ?? 0m;
        var closedAtUtc = trade.ClosedAtUtc ?? _timeProvider.GetUtcNow();

        _log.Add(new AlertDto(
            Id: Guid.NewGuid(),
            Kind: AlertKind.TradeClosed,
            Symbol: trade.Symbol,
            Message: AlertMessages.TradeClosed(trade.Symbol, @event.Outcome, realizedR),
            Direction: trade.Direction,
            Killzone: trade.Killzone,
            Style: trade.Style,
            AtUtc: closedAtUtc,
            Model: trade.Model));

        return Task.CompletedTask;
    }
}
