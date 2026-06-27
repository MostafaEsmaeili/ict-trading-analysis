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
        provider.GetRequiredService<IOptions<ConfluenceOptions>>().Value.EffectiveRequiredConditions.Should().NotBeEmpty();
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

    [Fact]
    public void Operator_active_killzones_actually_reach_the_entry_gate_hunt_set()
    {
        // [4] RECONCILIATION: the operator-facing `Ict:Scanning:ActiveKillzones` key now binds KillzoneEntryOptions
        // (the SINGLE source the KillzoneEntryDetector + EntryManager read), so narrowing to LondonOpen makes the
        // gate hunt ONLY LondonOpen — it previously read a separate, never-wired `Ict:Detection:Killzone` section.
        var provider = Build(("Ict:Scanning:ActiveKillzones:0", "LondonOpen"));

        var resolved = provider.GetRequiredService<IOptions<KillzoneEntryOptions>>().Value.ResolvedActiveKillzones;

        resolved.Should().ContainSingle().Which.Should().Be(IctTrader.Domain.Sessions.Killzone.LondonOpen);
    }

    [Fact]
    public void Unconfigured_active_killzones_fall_back_to_the_ict_london_new_york_default()
    {
        var provider = Build(); // no Ict:Scanning:ActiveKillzones configured

        var resolved = provider.GetRequiredService<IOptions<KillzoneEntryOptions>>().Value.ResolvedActiveKillzones;

        resolved.Should().Equal(
            IctTrader.Domain.Sessions.Killzone.LondonOpen, IctTrader.Domain.Sessions.Killzone.NewYorkOpen);
    }

    [Fact]
    public void Configured_loss_ladder_replaces_the_default_and_is_not_appended()
    {
        // [12] binder-append regression: `[0.2, 0.1]` must NOT bind to `[0.5, 0.25, 0.2, 0.1]`.
        var provider = Build(
            ("Ict:Risk:LossLadderPercents:0", "0.2"), ("Ict:Risk:LossLadderPercents:1", "0.1"));

        var resolved = provider.GetRequiredService<IOptions<RiskOptions>>().Value.ResolvedLossLadderPercents;

        resolved.Should().Equal(0.2m, 0.1m);
    }

    [Fact]
    public void Configured_sd_multiples_replace_the_default_and_are_not_appended()
    {
        // [14] binder-append regression: `[3.0, 4.0]` must NOT bind to `[1.0, 1.5, 2.0, 3.0, 4.0]`.
        var provider = Build(
            ("Ict:Detection:SdProjection:Multiples:0", "3.0"), ("Ict:Detection:SdProjection:Multiples:1", "4.0"));

        var resolved = provider.GetRequiredService<IOptions<SdProjectionOptions>>().Value.ResolvedMultiples;

        resolved.Should().Equal(3.0m, 4.0m);
    }

    [Fact]
    public void Configured_standing_conditions_replace_the_default_and_honor_a_removal()
    {
        // [15] binder-append regression: removing PremiumDiscountHalf must be honored, not silently re-added.
        var provider = Build(
            ("Ict:Scanning:Candidate:StandingConditions:0", "BiasAligned"),
            ("Ict:Scanning:Candidate:StandingConditions:1", "KillzoneEntry"),
            ("Ict:Scanning:Candidate:StandingConditions:2", "CalendarClear"));

        var resolved = provider.GetRequiredService<IOptions<SetupCandidateOptions>>().Value.ResolvedStandingConditions;

        resolved.Should().NotContain(IctTrader.Domain.Detection.ConfluenceCondition.PremiumDiscountHalf);
        resolved.Should().HaveCount(3);
    }

    [Fact]
    public void Configured_required_conditions_replace_the_default_and_honor_a_removal()
    {
        // [22] binder-append regression: dropping CalendarClear must be honored, not re-appended.
        var provider = Build(
            ("Ict:Confluence:RequiredConditions:0", "KillzoneEntry"),
            ("Ict:Confluence:RequiredConditions:1", "LiquiditySweep"));

        var effective = provider.GetRequiredService<IOptions<ConfluenceOptions>>().Value.EffectiveRequiredConditions;

        effective.Should().NotContain(IctTrader.Domain.Detection.ConfluenceCondition.CalendarClear);
        effective.Should().HaveCount(2);
    }

    [Fact]
    public void An_undefined_entry_mode_fails_validation_with_the_section()
    {
        // [23] enum guard: an out-of-range Ict:Execution:Entry:Mode fails fast at startup.
        var provider = Build(("Ict:Execution:Entry:Mode", "99"));

        var act = () => provider.GetRequiredService<IOptions<EntryManagementOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Ict:Execution:Entry*");
    }

    [Fact]
    public void Configured_oanda_instruments_replace_the_default_and_are_not_appended()
    {
        // [13] binder-append regression on OandaFeedOptions (bound from Ict:MarketData:Oanda via AddOandaFeed): an
        // operator selecting only GBP_USD must NOT silently still stream EUR_USD. Bind through the REAL binder here.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ict:MarketData:Oanda:Instruments:0"] = "GBP_USD",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<IctTrader.MarketData.Infrastructure.Feeds.OandaFeedOptions>()
            .Bind(config.GetSection(IctTrader.MarketData.Infrastructure.Feeds.OandaFeedOptions.SectionName));
        var provider = services.BuildServiceProvider();

        var resolved = provider
            .GetRequiredService<IOptions<IctTrader.MarketData.Infrastructure.Feeds.OandaFeedOptions>>()
            .Value.ResolvedInstruments;

        resolved.Should().ContainSingle().Which.Should().Be("GBP_USD");
    }
}
