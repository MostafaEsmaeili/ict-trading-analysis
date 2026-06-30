using System.Threading.Channels;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Application.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace IctTrader.MarketData.Application.Chart;

/// <summary>
/// Composition-root wiring for the MarketData module's Application read-models (plan §3.0a / §9.1). Registers the
/// <see cref="ChartCandleStore"/> as a SINGLETON so the bounded per-series chart window survives across bus
/// dispatches (the candle projection handler appends; the chart-candles query handler reads it).
///
/// <para>The bus handlers (<see cref="ChartCandleProjectionHandler"/>, <see cref="GetChartCandlesQueryHandler"/>,
/// <see cref="CandlePersistenceProjectionHandler"/>, <see cref="GetChartRangeQueryHandler"/>)
/// are NOT registered here — they are auto-discovered by <c>AddMessaging</c>'s assembly scan, provided the Host adds
/// this Application assembly to the scan set.</para>
///
/// <para><see cref="CandlePersistenceProjectionHandler"/> requires a <see cref="Channel{T}"/> of <see cref="Candle"/>.
/// <see cref="AddMarketDataReadModels"/> registers a SMALL fallback bounded channel via
/// <c>TryAddSingleton</c> so unit tests and environments without candle persistence never fail DI resolution.
/// The Host's <c>ScanLoopRegistration</c> registers the real (correctly-sized) channel BEFORE calling
/// <c>AddMessaging</c>, so the <c>TryAdd</c> here is a no-op in production.</para>
/// </summary>
public static class MarketDataModuleRegistration
{
    /// <summary>Capacity of the fallback channel registered for unit-test / persistence-off environments.</summary>
    private const int FallbackChannelCapacity = 16;

    public static IServiceCollection AddMarketDataReadModels(this IServiceCollection services)
    {
        services.AddSingleton<ChartCandleStore>();

        // Fallback channel: ensures CandlePersistenceProjectionHandler resolves in any DI container that
        // scans this assembly (e.g. unit tests that call AddMessaging without enabling candle persistence).
        // TryAddSingleton is a no-op when the Host has already registered the real production channel.
        services.TryAddSingleton<Channel<Candle>>(
            Channel.CreateBounded<Candle>(new BoundedChannelOptions(FallbackChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            }));

        // The persistence projection handler uses ILogger<T>. AddLogging (NullLoggerFactory by default)
        // ensures the handler resolves in unit tests that scan this assembly without a full Host.
        // In the real Host, AddLogging is called by WebApplication.CreateBuilder — TryAdd is a no-op.
        services.AddLogging();

        return services;
    }
}
