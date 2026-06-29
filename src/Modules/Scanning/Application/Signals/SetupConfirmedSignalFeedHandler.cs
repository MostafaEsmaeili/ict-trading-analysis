using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Scanning.Application.Signals;

/// <summary>
/// The signals feed's setup sink (plan §3.0a / §9): it reacts to each <see cref="SetupConfirmed"/> (published by
/// <see cref="Scanning.CandleIngestedHandler"/>), adds the confirmed advisory setup to the singleton
/// <see cref="SignalFeedStore"/>, recomputes the ranked top-N via the <see cref="SignalRankingService"/>, and
/// publishes a <see cref="SignalsUpdated"/> so the dashboard's "best opportunities" feed updates live over SignalR.
/// The handler ORCHESTRATES only — the ranking DECISION lives in the pure domain ranker the service wraps. The bus
/// fans <see cref="SetupConfirmed"/> out to ALL subscribers, so this coexists with the chart-overlay, Alerting and
/// PaperTrading consumers of the same event.
///
/// <para>Read-only sink (plan §6.3 guardrail): adding a confirmed advisory setup to a ranked feed routes nowhere near
/// an order path.</para>
/// </summary>
public sealed class SetupConfirmedSignalFeedHandler(
    SignalFeedStore store, SignalRankingService ranking, IMessageBus bus, TimeProvider timeProvider)
    : IEventHandler<SetupConfirmed>
{
    private readonly SignalFeedStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly SignalRankingService _ranking = ranking ?? throw new ArgumentNullException(nameof(ranking));
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task HandleAsync(SetupConfirmed @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var now = _timeProvider.GetUtcNow();
        _store.Add(@event.Setup, now);

        // Push the recomputed unfiltered top-N so the live feed mirrors the default REST feed.
        var top = _ranking.Top(now);
        await _bus.PublishAsync(new SignalsUpdated(top), cancellationToken).ConfigureAwait(false);
    }
}
