using IctTrader.MarketData.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.MarketData.Application.Chart;

/// <summary>
/// The chart read-model's candle sink (plan §3.0a / §9.1): it reacts to each <see cref="CandleIngested"/> from the
/// MarketData feed and appends the candle to the singleton <see cref="ChartCandleStore"/>, so the dashboard's ICT
/// Pattern Chart can serve REAL bars over the bus. The handler MAPS only — it adds no business logic.
///
/// <para>Read-only sink (plan §6.3 guardrail): projecting a read-only OHLC bar into the chart window routes nowhere
/// near an order path.</para>
/// </summary>
public sealed class ChartCandleProjectionHandler(ChartCandleStore store) : IEventHandler<CandleIngested>
{
    private readonly ChartCandleStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public Task HandleAsync(CandleIngested @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        _store.Append(@event.Candle);
        return Task.CompletedTask;
    }
}
