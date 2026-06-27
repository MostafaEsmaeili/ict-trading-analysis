using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IctTrader.Host.Calendar;

/// <summary>
/// Composes the economic-calendar feed (plan §2.5.8/§15): the shared <see cref="IEconomicCalendarStore"/>, the
/// configured <see cref="IEconomicCalendarSource"/> (operator-supplied Config, or the FMP HTTP provider), and the
/// background loader that keeps the store current so the §2.5.2 gate fires on real FOMC/NFP days. The store is the
/// SAME singleton the Scanning module resolves, so a load reaches every per-symbol scanner. When the feed is
/// disabled (default) only the empty store is present and the gate stays in its unverified posture (fail-open).
/// Read-only throughout — a calendar source has no order/broker surface (§6.3 guardrail).
/// </summary>
public static class EconomicCalendarRegistration
{
    public static IServiceCollection AddEconomicCalendar(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CalendarFeedOptions>()
            .Bind(configuration.GetSection(CalendarFeedOptions.SectionName))
            .Validate(
                options => options.Validate().Count == 0,
                $"{CalendarFeedOptions.SectionName} configuration is invalid.")
            .ValidateOnStart();

        // The shared store (the same singleton the Scanning module resolves; TryAdd so order does not matter).
        services.TryAddSingleton<IEconomicCalendarStore, EconomicCalendarStore>();

        var options = configuration.GetSection(CalendarFeedOptions.SectionName).Get<CalendarFeedOptions>()
            ?? new CalendarFeedOptions();

        // Nothing to load when disabled — the empty store stays unloaded → the gate is fail-open (its default).
        if (!options.Enabled)
        {
            return services;
        }

        AddSelectedSource(services, options.Provider);
        services.AddHostedService<EconomicCalendarLoaderHostedService>();
        return services;
    }

    /// <summary>Registers the selected read-only calendar source. Config serves operator-supplied dates (no network);
    /// FMP is a typed-HttpClient HTTP provider (base URL configured; the api-key rides the request query).</summary>
    private static void AddSelectedSource(IServiceCollection services, CalendarProvider provider)
    {
        switch (provider)
        {
            case CalendarProvider.Config:
                services.AddSingleton<IEconomicCalendarSource, ConfigEconomicCalendarSource>();
                break;

            case CalendarProvider.Fmp:
                services.AddHttpClient<FmpEconomicCalendarSource>(static (sp, client) =>
                {
                    var options = sp.GetRequiredService<IOptions<CalendarFeedOptions>>().Value;
                    client.BaseAddress = new Uri(options.Fmp.BaseUrl, UriKind.Absolute);
                });
                services.AddTransient<IEconomicCalendarSource>(
                    static sp => sp.GetRequiredService<FmpEconomicCalendarSource>());
                break;

            default:
                throw new InvalidOperationException(
                    $"{CalendarFeedOptions.SectionName}:Provider '{provider}' is not a supported calendar source.");
        }
    }
}
