using IctTrader.Domain.Instruments;
using IctTrader.PaperTrading.Application.Trading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IctTrader.PaperTrading.Application;

/// <summary>
/// Composition-root wiring for the PaperTrading module's Application layer (plan §3.0a). Registers the stateless
/// trade-orchestration machinery: the <see cref="ITradeOrchestratorFactory"/> (builds a per-symbol orchestrator from
/// the validated <c>Ict:*</c> options) and the <see cref="ITradeOrchestratorRegistry"/> (the per-symbol cache) as
/// SINGLETONS, the <see cref="IPaperAccountProvider"/> as SCOPED (it depends on the scoped repository — DB-as-state),
/// and binds + validates the module's own <see cref="PaperTradingOptions"/> (<c>Ict:PaperTrading</c>).
///
/// <para>The two bus handlers (<see cref="Trading.SetupConfirmedHandler"/>, <see cref="Trading.PaperTradingCandleHandler"/>)
/// are NOT registered here — they are auto-discovered by <c>AddMessaging</c>'s assembly scan as scoped
/// <c>IEventHandler</c>s.</para>
///
/// <para><b>Persistence is wired separately.</b> The 2d-ii repositories live in
/// <c>IctTrader.PaperTrading.Infrastructure</c>, which references this Application assembly — so calling
/// <c>AddPaperTradingPersistence()</c> from here would create a circular project reference. The Host composes both
/// (this module + <c>AddPaperTradingPersistence()</c> + the <c>PaperTradingDbContext</c> with the connection string)
/// in slice 2e; this method registers only the Application services + options the handlers need.</para>
/// </summary>
public static class PaperTradingModuleRegistration
{
    public static IServiceCollection AddPaperTradingModule(this IServiceCollection services)
    {
        // The per-instrument catalog (§2.5.7 FX-vs-index resolution) — shared with Scanning, registered idempotently
        // (TryAdd) so module wiring order does not matter. The orchestrator factory resolves each symbol's profile.
        services.TryAddSingleton<IInstrumentRegistry>(InstrumentCatalog.Default);
        services.AddSingleton<ITradeOrchestratorFactory, TradeOrchestratorFactory>();
        services.AddSingleton<ITradeOrchestratorRegistry, TradeOrchestratorRegistry>();
        services.AddScoped<IPaperAccountProvider, PaperAccountProvider>();

        services
            .AddOptions<PaperTradingOptions>()
            .BindConfiguration(PaperTradingOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PaperTradingOptions>>(
            new PaperTradingOptionsValidator());

        return services;
    }

    /// <summary>Fails startup (via <c>ValidateOnStart</c>) with a section-qualified reason if the
    /// <c>Ict:PaperTrading</c> config is invalid — delegating to the POCO's own <see cref="PaperTradingOptions.Validate"/>.</summary>
    private sealed class PaperTradingOptionsValidator : IValidateOptions<PaperTradingOptions>
    {
        public ValidateOptionsResult Validate(string? name, PaperTradingOptions options)
        {
            var errors = options.Validate();
            return errors.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(errors.Select(e => $"{PaperTradingOptions.SectionName}: {e}"));
        }
    }
}
