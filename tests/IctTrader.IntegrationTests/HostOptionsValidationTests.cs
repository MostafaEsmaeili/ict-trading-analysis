using IctTrader.Domain.Configuration;
using IctTrader.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Locks WP7 slice 1 — the Host binds every <c>Ict:*</c> options POCO to its section and self-validates it at
/// startup (plan §4.6 "no magic numbers" + fail-fast). The verified POCO defaults bind clean out of the box, and an
/// operator override that breaks a contract fails with the section-qualified reason rather than silently mis-running.
/// </summary>
public class HostOptionsValidationTests
{
    private static IServiceProvider Build(params (string Key, string Value)[] overrides)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(overrides.ToDictionary(o => o.Key, o => (string?)o.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddIctOptions(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Default_configuration_binds_and_validates_every_ict_options_section()
    {
        var provider = Build();

        // Resolving .Value runs the registered IValidateOptions<T>; the verified POCO defaults must all pass.
        provider.GetRequiredService<IOptions<RiskOptions>>().Value.BaseRiskPercent.Should().Be(1.0m);
        provider.GetRequiredService<IOptions<FvgOptions>>().Value.VoidOnTouchCount.Should().Be(3);
        provider.GetRequiredService<IOptions<DisplacementOptions>>().Value.DisplacementLegMaxBars.Should().Be(3);
        provider.GetRequiredService<IOptions<EntryManagementOptions>>().Value.UseCloseProximityEntry.Should().BeFalse();
        provider.GetRequiredService<IOptions<SdProjectionOptions>>().Value.Enabled.Should().BeFalse();
        provider.GetRequiredService<IOptions<ConfluenceOptions>>().Value.RequiredConditions.Should().NotBeEmpty();
    }

    [Fact]
    public void A_configuration_override_that_breaks_a_contract_fails_validation_with_the_section()
    {
        var provider = Build(("Ict:Risk:BaseRiskPercent", "0")); // 0% risk is out of contract

        var act = () => provider.GetRequiredService<IOptions<RiskOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Ict:Risk*");
    }

    [Fact]
    public void A_displacement_gate_override_out_of_range_fails_validation()
    {
        var provider = Build(("Ict:Displacement:DisplacementLegMaxBars", "0")); // a leg must span at least one bar

        var act = () => provider.GetRequiredService<IOptions<DisplacementOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Ict:Displacement*");
    }

    [Fact]
    public void Configured_active_styles_replace_the_default_and_are_not_duplicated()
    {
        // REGRESSION: the .NET configuration binder APPENDS bound array items onto a pre-populated collection
        // initializer. A non-empty ActiveStyles default therefore turned `["Intraday"]` into `[Intraday, Intraday]`,
        // so the candle handler fed every candle to the same scanner twice and no setup ever confirmed. The fix
        // defaults the list to empty (so the binder replaces) and resolves the ICT default via ResolvedActiveStyles.
        var provider = Build(("Ict:Scanning:ActiveStyles:0", "Intraday"));

        var resolved = provider.GetRequiredService<IOptions<MarketContextOptions>>().Value.ResolvedActiveStyles;

        resolved.Should().ContainSingle().Which.Should().Be(IctTrader.Domain.Styles.TradeStyle.Intraday);
    }

    [Fact]
    public void A_non_default_active_style_does_not_silently_re_add_the_default()
    {
        // The default must never be PREPENDED to an operator's selection — choosing Scalp must run ONLY Scalp.
        var provider = Build(("Ict:Scanning:ActiveStyles:0", "Scalp"));

        var resolved = provider.GetRequiredService<IOptions<MarketContextOptions>>().Value.ResolvedActiveStyles;

        resolved.Should().ContainSingle().Which.Should().Be(IctTrader.Domain.Styles.TradeStyle.Scalp);
    }

    [Fact]
    public void Unconfigured_active_styles_fall_back_to_the_ict_intraday_default()
    {
        var provider = Build(); // no Ict:Scanning:ActiveStyles configured

        var resolved = provider.GetRequiredService<IOptions<MarketContextOptions>>().Value.ResolvedActiveStyles;

        resolved.Should().ContainSingle().Which.Should().Be(IctTrader.Domain.Styles.TradeStyle.Intraday);
    }
}
