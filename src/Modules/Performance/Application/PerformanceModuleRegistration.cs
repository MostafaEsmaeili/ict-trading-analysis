using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.Performance.Application;

/// <summary>
/// Composition-root wiring for the Performance module's Application layer (plan §3.0a/§5.3). Registers the
/// <see cref="PerformanceState"/> as a SINGLETON so the accumulated closed-trade R stream survives across bus
/// dispatches (the closed-trade handler appends; the summary + equity-curve query handlers read it).
///
/// <para>The three bus handlers (<see cref="PaperTradeClosedHandler"/>, <see cref="GetPerformanceSummaryQueryHandler"/>,
/// <see cref="GetEquityCurveQueryHandler"/>) are NOT registered here — they are auto-discovered by <c>AddMessaging</c>'s
/// assembly scan, provided the Host adds this Application assembly to the scan set. The pure
/// <see cref="Domain.Services.PerformanceCalculator"/> is a static domain service (no DI registration needed).</para>
///
/// <para>No options POCO: this cut has no tunable constant beyond the named domain consts (the profit-factor sentinel,
/// the equity baseline) that live on the calculator. State is in-memory — persistence / warm-start is deferred (§5.3).</para>
/// </summary>
public static class PerformanceModuleRegistration
{
    public static IServiceCollection AddPerformanceModule(this IServiceCollection services)
    {
        services.AddSingleton<PerformanceState>();
        return services;
    }
}
