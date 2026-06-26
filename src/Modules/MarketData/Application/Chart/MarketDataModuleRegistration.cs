using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.MarketData.Application.Chart;

/// <summary>
/// Composition-root wiring for the MarketData module's Application read-models (plan §3.0a / §9.1). Registers the
/// <see cref="ChartCandleStore"/> as a SINGLETON so the bounded per-series chart window survives across bus
/// dispatches (the candle projection handler appends; the chart-candles query handler reads it).
///
/// <para>The two bus handlers (<see cref="ChartCandleProjectionHandler"/>, <see cref="GetChartCandlesQueryHandler"/>)
/// are NOT registered here — they are auto-discovered by <c>AddMessaging</c>'s assembly scan, provided the Host adds
/// this Application assembly to the scan set.</para>
///
/// <para>No options POCO: this cut has no tunable constant beyond the named <see cref="ChartCandleStore.MaxCandlesPerSeries"/>
/// cap. State is in-memory — persistence / warm-start is deferred.</para>
/// </summary>
public static class MarketDataModuleRegistration
{
    public static IServiceCollection AddMarketDataReadModels(this IServiceCollection services)
    {
        services.AddSingleton<ChartCandleStore>();
        return services;
    }
}
