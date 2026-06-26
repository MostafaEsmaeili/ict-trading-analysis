using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// The chart overlay read-model's setup sink (plan §3.0a / §9.1): it reacts to each <see cref="SetupConfirmed"/>
/// (published by <see cref="CandleIngestedHandler"/>) and adds the confirmed, advisory setup to the singleton
/// <see cref="RecentSetupStore"/>, so the dashboard's ICT Pattern Chart can overlay REAL setups over the bus. The
/// handler MAPS only — it adds no business logic. The bus fans <see cref="SetupConfirmed"/> out to ALL subscribers,
/// so this read-model handler coexists with the Alerting and PaperTrading consumers of the same event.
///
/// <para>Read-only sink (plan §6.3 guardrail): surfacing a confirmed advisory setup as a chart overlay routes
/// nowhere near an order path.</para>
/// </summary>
public sealed class SetupConfirmedChartProjectionHandler(RecentSetupStore store) : IEventHandler<SetupConfirmed>
{
    private readonly RecentSetupStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public Task HandleAsync(SetupConfirmed @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        _store.Add(@event.Setup);
        return Task.CompletedTask;
    }
}
