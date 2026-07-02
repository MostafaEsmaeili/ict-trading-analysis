using IctTrader.Alerting.Contracts;
using IctTrader.PaperTrading.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Alerting.Application;

/// <summary>
/// The Alerting module's trade-opened sink (plan §3.0a / §9): it reacts to each <see cref="PaperTradeOpened"/>
/// from the PaperTrading module and projects the SIMULATED trade into a concise <see cref="AlertDto"/> appended
/// to the singleton <see cref="AlertLog"/>, so the dashboard's Alerts feed shows when a paper position opened.
/// The handler MAPS only.
///
/// <para><see cref="AlertDto.Killzone"/> is null: the <see cref="PaperTradeDto"/> may not carry the originating
/// killzone (the setup alert already records it), so the trade alert leaves it unset rather than inventing one.</para>
///
/// <para>Read-only sink (plan §6.3 guardrail): the trade is a SIMULATED paper trade — there is no live counterpart
/// anywhere — and surfacing it as a notification routes nowhere near an order path.</para>
/// </summary>
public sealed class PaperTradeOpenedAlertHandler(AlertLog log) : IEventHandler<PaperTradeOpened>
{
    private readonly AlertLog _log = log ?? throw new ArgumentNullException(nameof(log));

    public Task HandleAsync(PaperTradeOpened @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var trade = @event.Trade;
        _log.Add(new AlertDto(
            Id: Guid.NewGuid(),
            Kind: AlertKind.TradeOpened,
            Symbol: trade.Symbol,
            Message: AlertMessages.TradeOpened(trade.Direction, trade.Symbol, trade.Entry),
            Direction: trade.Direction,
            Killzone: trade.Killzone,
            Style: trade.Style,
            AtUtc: trade.OpenedAtUtc,
            Model: trade.Model));

        return Task.CompletedTask;
    }
}
