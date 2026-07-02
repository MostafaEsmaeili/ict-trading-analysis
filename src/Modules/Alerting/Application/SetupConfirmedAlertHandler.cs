using IctTrader.Alerting.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Alerting.Application;

/// <summary>
/// The Alerting module's setup sink (plan §3.0a / §9): it reacts to each <see cref="SetupConfirmed"/> from
/// the Scanning module and projects the confirmed, advisory setup into an <see cref="AlertDto"/> appended to
/// the singleton <see cref="AlertLog"/>, so the dashboard's Alerts feed surfaces the §2.5 reasoning the moment
/// a setup confirms. The handler MAPS only — it adds no business logic.
///
/// <para>The alert <see cref="AlertDto.Message"/> is the setup's own §2.5 <see cref="SetupDto.Reason"/> verbatim
/// (the rank-sorted confluence clauses + priced plan summary), so the feed shows exactly why the setup graded.</para>
///
/// <para>Read-only sink (plan §6.3 guardrail): surfacing a confirmed advisory setup as a notification routes
/// nowhere near an order path.</para>
/// </summary>
public sealed class SetupConfirmedAlertHandler(AlertLog log) : IEventHandler<SetupConfirmed>
{
    private readonly AlertLog _log = log ?? throw new ArgumentNullException(nameof(log));

    public Task HandleAsync(SetupConfirmed @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var setup = @event.Setup;
        _log.Add(new AlertDto(
            Id: Guid.NewGuid(),
            Kind: AlertKind.Setup,
            Symbol: setup.Symbol,
            Message: setup.Reason,
            Direction: setup.Direction,
            Killzone: setup.Killzone,
            Style: setup.Style,
            AtUtc: setup.DetectedAtUtc,
            Model: setup.Model));

        return Task.CompletedTask;
    }
}
