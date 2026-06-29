using IctTrader.Domain.Configuration;
using IctTrader.MarketData.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace IctTrader.Host;

/// <summary>
/// A generic <see cref="IValidateOptions{TOptions}"/> that delegates to an options POCO's own <c>Validate()</c>
/// method (plan §4.6 "no magic numbers" — every `Ict:*` constant is operator-tunable but startup-validated). A
/// non-empty error list fails startup via <c>ValidateOnStart</c>, so a mis-configured host never silently mis-times
/// killzones, under-sizes risk, or breaks an invariant — it fails fast with the section-qualified reasons.
/// </summary>
internal sealed class IctOptionsValidator<TOptions>(string section, Func<TOptions, IReadOnlyList<string>> validate)
    : IValidateOptions<TOptions>
    where TOptions : class
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        var errors = validate(options);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors.Select(e => $"{section}: {e}"));
    }
}

/// <summary>
/// Binds every <c>Ict:*</c> options POCO to its configuration section and registers its self-validation with
/// <c>ValidateOnStart</c> (plan §4.6, WP7). Each POCO carries its own <c>SectionName</c> + <c>Validate()</c>; binding
/// an absent section leaves the POCO's verified defaults, so a minimal appsettings is valid out of the box and an
/// operator override is checked against the same contract the unit tests pin.
/// </summary>
public static class IctOptionsRegistration
{
    private static IServiceCollection AddIctOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration config,
        string section,
        Func<TOptions, IReadOnlyList<string>> validate)
        where TOptions : class
    {
        services.AddOptions<TOptions>().Bind(config.GetSection(section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TOptions>>(new IctOptionsValidator<TOptions>(section, validate));
        return services;
    }

    public static IServiceCollection AddIctOptions(this IServiceCollection services, IConfiguration config)
        => services
            // Confluence + scanning
            .AddIctOptions<ConfluenceOptions>(config, ConfluenceOptions.SectionName, o => o.Validate())
            .AddIctOptions<MarketContextOptions>(config, MarketContextOptions.SectionName, o => o.Validate())
            .AddIctOptions<SetupCandidateOptions>(config, SetupCandidateOptions.SectionName, o => o.Validate())
            .AddIctOptions<SilverBulletOptions>(config, SilverBulletOptions.SectionName, o => o.Validate())
            .AddIctOptions<SignalRankingOptions>(config, SignalRankingOptions.SectionName, o => o.Validate())
            .AddIctOptions<TradeStyleOptions>(config, TradeStyleOptions.SectionName, o => o.Validate())
            // Detection
            .AddIctOptions<DisplacementOptions>(config, DisplacementOptions.SectionName, o => o.Validate())
            .AddIctOptions<FvgOptions>(config, FvgOptions.SectionName, o => o.Validate())
            .AddIctOptions<OrderBlockOptions>(config, OrderBlockOptions.SectionName, o => o.Validate())
            .AddIctOptions<MarketStructureShiftOptions>(config, MarketStructureShiftOptions.SectionName, o => o.Validate())
            .AddIctOptions<LiquidityOptions>(config, LiquidityOptions.SectionName, o => o.Validate())
            .AddIctOptions<SwingOptions>(config, SwingOptions.SectionName, o => o.Validate())
            .AddIctOptions<OteOptions>(config, OteOptions.SectionName, o => o.Validate())
            .AddIctOptions<DailyBiasOptions>(config, DailyBiasOptions.SectionName, o => o.Validate())
            .AddIctOptions<PremiumDiscountOptions>(config, PremiumDiscountOptions.SectionName, o => o.Validate())
            .AddIctOptions<KillzoneEntryOptions>(config, KillzoneEntryOptions.SectionName, o => o.Validate())
            .AddIctOptions<CalendarOptions>(config, CalendarOptions.SectionName, o => o.Validate())
            .AddIctOptions<DrawOnLiquidityOptions>(config, DrawOnLiquidityOptions.SectionName, o => o.Validate())
            .AddIctOptions<TargetLadderOptions>(config, TargetLadderOptions.SectionName, o => o.Validate())
            .AddIctOptions<SdProjectionOptions>(config, SdProjectionOptions.SectionName, o => o.Validate())
            // Optional §2.5.3 confluences (the Grade-A enablers)
            .AddIctOptions<OpenPriceReferenceOptions>(config, OpenPriceReferenceOptions.SectionName, o => o.Validate())
            .AddIctOptions<MacroTimeOptions>(config, MacroTimeOptions.SectionName, o => o.Validate())
            .AddIctOptions<CleanPriceActionOptions>(config, CleanPriceActionOptions.SectionName, o => o.Validate())
            .AddIctOptions<CalendarDriverOptions>(config, CalendarDriverOptions.SectionName, o => o.Validate())
            // Risk + execution
            .AddIctOptions<RiskOptions>(config, RiskOptions.SectionName, o => o.Validate())
            .AddIctOptions<DailyRiskGuardOptions>(config, DailyRiskGuardOptions.SectionName, o => o.Validate())
            .AddIctOptions<ExecutionCostOptions>(config, ExecutionCostOptions.SectionName, o => o.Validate())
            .AddIctOptions<FillOptions>(config, FillOptions.SectionName, o => o.Validate())
            .AddIctOptions<ExitManagementOptions>(config, ExitManagementOptions.SectionName, o => o.Validate())
            .AddIctOptions<StopTrailOptions>(config, StopTrailOptions.SectionName, o => o.Validate())
            .AddIctOptions<EntryManagementOptions>(config, EntryManagementOptions.SectionName, o => o.Validate())
            // Per-instrument overrides (the baked per-pair tuning results, e.g. NAS100 → 6-of-8)
            .AddIctOptions<InstrumentOverridesOptions>(config, InstrumentOverridesOptions.SectionName, o => o.Validate())
            // Candle persistence (plan §7 — the batched dual-write background writer)
            .AddIctOptions<CandlePersistenceOptions>(config, CandlePersistenceOptions.SectionName, o => o.Validate());
}
