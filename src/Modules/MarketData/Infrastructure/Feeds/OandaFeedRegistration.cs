using System.Net.Http.Headers;
using IctTrader.MarketData.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IctTrader.MarketData.Infrastructure.Feeds;

/// <summary>
/// Registers the <b>read-only</b> OANDA-practice market-data feed into the DI container (plan §6, WP7). Binds and
/// startup-validates <see cref="OandaFeedOptions"/>, configures a typed <see cref="HttpClient"/> with the
/// practice base URL + the <c>Authorization: Bearer</c> token from the bound options, and registers
/// <see cref="OandaMarketDataFeed"/> as the <see cref="IMarketDataFeed"/>.
/// <para>
/// The Host chooses Replay vs OANDA by configuration in a follow-on slice — this extension only exposes the
/// OANDA wiring; it does not touch the Host. No order/account services are registered because the feed has no
/// order path (the guardrail is structural).
/// </para>
/// </summary>
public static class OandaFeedRegistration
{
    public static IServiceCollection AddOandaFeed(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<OandaFeedOptions>()
            .Bind(configuration.GetSection(OandaFeedOptions.SectionName))
            .Validate(
                options => options.Validate().Count == 0,
                $"{OandaFeedOptions.SectionName} configuration is invalid.")
            .ValidateOnStart();

        // Expose the validated, bound options as a resolvable singleton so the typed-client factory can inject
        // them into the feed's (HttpClient, OandaFeedOptions, TimeProvider) constructor.
        services.AddSingleton(
            static provider => provider.GetRequiredService<IOptions<OandaFeedOptions>>().Value);

        services
            .AddHttpClient<OandaMarketDataFeed>(static (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<OandaFeedOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", options.Token);
            });

        // Resolve IMarketDataFeed from the typed-client registration so the feed shares IHttpClientFactory's
        // handler lifetime (transient, matching AddHttpClient) rather than capturing one handler forever.
        services.AddTransient<IMarketDataFeed>(
            static provider => provider.GetRequiredService<OandaMarketDataFeed>());

        return services;
    }

    /// <summary>
    /// Registers the <b>read-only</b> one-shot OANDA history fetcher (issue #100): binds + startup-validates
    /// <see cref="OandaFeedOptions"/>, configures a typed <see cref="HttpClient"/> with the practice base URL +
    /// the <c>Authorization: Bearer</c> token, and registers <see cref="OandaHistoryFetcher"/>. It does NOT register
    /// an <see cref="IMarketDataFeed"/>, so the normal scan-loop ingestion does not run — this is the export tool.
    /// No order/account services are registered because the fetcher has no order path (the guardrail is structural).
    /// </summary>
    public static IServiceCollection AddOandaHistoryFetcher(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<OandaFeedOptions>()
            .Bind(configuration.GetSection(OandaFeedOptions.SectionName))
            .Validate(
                options => options.Validate().Count == 0,
                $"{OandaFeedOptions.SectionName} configuration is invalid.")
            .ValidateOnStart();

        // Expose the validated, bound options as a resolvable singleton (mirrors AddOandaFeed) so callers — e.g. the
        // history-fetch hosted service — can read Instruments/Granularity/HistoryMaxCandles/HistoryOutputDirectory.
        services.AddSingleton(
            static provider => provider.GetRequiredService<IOptions<OandaFeedOptions>>().Value);

        services
            .AddHttpClient<OandaHistoryFetcher>(static (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<OandaFeedOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", options.Token);
            });

        return services;
    }
}
