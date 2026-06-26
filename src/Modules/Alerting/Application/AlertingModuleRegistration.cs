using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.Alerting.Application;

/// <summary>
/// Composition-root wiring for the Alerting module's Application layer (plan §3.0a / §9). Registers the
/// <see cref="AlertLog"/> as a SINGLETON so the bounded recent-alerts feed survives across bus dispatches (the
/// setup/trade event handlers append; the recent-alerts query handler reads it).
///
/// <para>The four bus handlers (<see cref="SetupConfirmedAlertHandler"/>, <see cref="PaperTradeOpenedAlertHandler"/>,
/// <see cref="PaperTradeClosedAlertHandler"/>, <see cref="GetRecentAlertsQueryHandler"/>) are NOT registered here —
/// they are auto-discovered by <c>AddMessaging</c>'s assembly scan, provided the Host adds this Application assembly
/// to the scan set.</para>
///
/// <para>No options POCO: this cut has no tunable constant beyond the named <see cref="AlertLog.MaxAlerts"/> cap and
/// the <see cref="AlertKind"/> discriminators. State is in-memory — persistence / warm-start is deferred.</para>
/// </summary>
public static class AlertingModuleRegistration
{
    public static IServiceCollection AddAlertingModule(this IServiceCollection services)
    {
        services.AddSingleton<AlertLog>();
        return services;
    }
}
