using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;

namespace IctTrader.UnitTests.Configuration;

/// <summary>
/// Locks the Options self-validation tightened in the WP1 review pass (plan §4.6 "no magic numbers" +
/// ValidateOnStart): every operator-tunable gate must fail startup when configured out of contract, the
/// ActiveKillzones set must stay within the FROZEN selectable subset, and the trade-style resolver must
/// never silently fall back to Intraday for an unknown style.
/// </summary>
public class OptionsValidationTests
{
    [Fact]
    public void Default_options_validate_clean()
    {
        new ConfluenceOptions().Validate().Should().BeEmpty();
        new DisplacementOptions().Validate().Should().BeEmpty();
        new FvgOptions().Validate().Should().BeEmpty();
        new OrderBlockOptions().Validate().Should().BeEmpty();
        new MarketContextOptions().Validate().Should().BeEmpty();
        new TradeStyleOptions().Validate().Should().BeEmpty();
        new SetupCandidateOptions().Validate().Should().BeEmpty();
        new KillzoneEntryOptions().Validate().Should().BeEmpty();
        new DrawOnLiquidityOptions().Validate().Should().BeEmpty();
        new TargetLadderOptions().Validate().Should().BeEmpty();
        new RiskOptions().Validate().Should().BeEmpty();
        new FillOptions().Validate().Should().BeEmpty();
        new ExecutionCostOptions().Validate().Should().BeEmpty();
        new ExitManagementOptions().Validate().Should().BeEmpty();
        new StopTrailOptions().Validate().Should().BeEmpty();
        new EntryManagementOptions().Validate().Should().BeEmpty();
        new SdProjectionOptions().Validate().Should().BeEmpty();
        new OpenPriceReferenceOptions().Validate().Should().BeEmpty();
        new MacroTimeOptions().Validate().Should().BeEmpty();
        new CleanPriceActionOptions().Validate().Should().BeEmpty();
        new CalendarDriverOptions().Validate().Should().BeEmpty();
        new DailyRiskGuardOptions().Validate().Should().BeEmpty();
    }

    [Fact]
    public void The_daily_risk_guard_defaults_are_off_and_transcript_honest()
    {
        var defaults = new DailyRiskGuardOptions();
        defaults.Enabled.Should().BeFalse();                    // config-default OFF — existing backtests stay byte-identical
        defaults.ConsecutiveLossHaltThreshold.Should().Be(3);   // the 1%→0.5%→0.25% ladder exhausted (Ep41)
        defaults.DailyLossCapPercent.Should().Be(2.0m);         // ≈ the 1.75% disciplined-ladder loss, rounded
        defaults.ResetAtNyDayRollover.Should().BeTrue();
    }

    [Fact]
    public void A_daily_risk_guard_out_of_contract_is_rejected()
    {
        new DailyRiskGuardOptions { ConsecutiveLossHaltThreshold = 0 }.Validate().Should().NotBeEmpty(); // must be ≥ 1
        new DailyRiskGuardOptions { DailyLossCapPercent = 0m }.Validate().Should().NotBeEmpty();         // must be > 0
        new DailyRiskGuardOptions { DailyLossCapPercent = 25m }.Validate().Should().NotBeEmpty();        // beyond the 10% ceiling
    }

    [Fact]
    public void Sd_projection_multiples_and_negative_fib_coefficients_must_be_in_contract()
    {
        new SdProjectionOptions { Multiples = [1.5m, 1.0m] }.Validate().Should().NotBeEmpty();       // not ascending
        new SdProjectionOptions { Multiples = [0m, 1.0m] }.Validate().Should().NotBeEmpty();         // non-positive
        new SdProjectionOptions
        {
            NegativeFibVariant = new NegativeFibOptions { Enabled = true, Coefficients = [0.5m, 1.5m] },
        }.Validate().Should().NotBeEmpty();                                                          // coefficient > 1
    }

    [Fact]
    public void A_non_positive_entry_max_wait_is_rejected()
        => new EntryManagementOptions { MaxWaitMinutes = 0 }.Validate().Should().NotBeEmpty();

    [Fact]
    public void The_eg3_close_proximity_flag_defaults_off_and_its_tolerance_must_be_non_negative()
    {
        // EG-3 v1: the close-proximity fill is an additive, default-OFF flag; the tolerance is INVENTED but must be ≥ 0.
        new EntryManagementOptions().UseCloseProximityEntry.Should().BeFalse();
        new EntryManagementOptions().CloseProximityTolerancePips.Should().Be(2m);
        new EntryManagementOptions { UseCloseProximityEntry = true }.Validate().Should().BeEmpty();
        new EntryManagementOptions { CloseProximityTolerancePips = -0.1m }.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void An_out_of_order_or_non_positive_trail_ladder_is_rejected()
    {
        new StopTrailOptions { TrailHalfwayFraction = 0.80m, TrailBreakevenFraction = 0.75m }
            .Validate().Should().NotBeEmpty(); // halfway must sit below breakeven
        new StopTrailOptions { BreakEvenAtR = 0m }.Validate().Should().NotBeEmpty(); // BreakEvenAtR must be positive
    }

    [Fact]
    public void A_partial_fraction_outside_the_open_unit_interval_is_rejected()
    {
        new ExitManagementOptions { PartialFraction = 0m }.Validate().Should().NotBeEmpty();
        new ExitManagementOptions { PartialFraction = 1m }.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void The_deferred_fx_close_no_overnight_boundary_is_rejected()
        => new ExitManagementOptions { NoOvernightBoundary = NoOvernightBoundary.NyFxClose1700 }
            .Validate().Should().NotBeEmpty();

    [Fact]
    public void An_undefined_intrabar_fill_assumption_is_rejected()
        => new FillOptions { StopVsTarget = (IntrabarFillAssumption)99 }.Validate().Should().NotBeEmpty();

    [Fact]
    public void Negative_execution_costs_are_rejected()
    {
        new ExecutionCostOptions { Spread = new SpreadOptions { BasePips = -0.1m } }.Validate().Should().NotBeEmpty();
        new ExecutionCostOptions { Commission = new CommissionOptions { PerLotRoundTripUsd = -1m } }
            .Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void Risk_percentages_outside_their_contract_are_rejected()
    {
        new RiskOptions { BaseRiskPercent = 0m }.Validate().Should().NotBeEmpty();
        new RiskOptions { MaxOpenPortfolioRiskPercent = 0m }.Validate().Should().NotBeEmpty();
        new RiskOptions { MinStopDistancePips = 0m }.Validate().Should().NotBeEmpty();
        // a per-trade risk above the aggregate portfolio cap is incoherent
        new RiskOptions { BaseRiskPercent = 6m, MaxOpenPortfolioRiskPercent = 5m }.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void Adaptive_risk_ladder_settings_outside_their_contract_are_rejected()
    {
        new RiskOptions { LossLadderPercents = [0.5m, 0.6m] }.Validate().Should().NotBeEmpty();   // not descending
        new RiskOptions { LossLadderPercents = [1.0m, 0.5m] }.Validate().Should().NotBeEmpty();   // first step not below base
        new RiskOptions { LossLadderPercents = [] }.Validate().Should().BeEmpty();                // empty = use the §2.5.5 default
        new RiskOptions { DipRecoveryFraction = 0m }.Validate().Should().NotBeEmpty();            // out of (0, 1]
        new RiskOptions { DipRecoveryFraction = 1.5m }.Validate().Should().NotBeEmpty();
        new RiskOptions { ConsecutiveWinsForLowestUnit = 0 }.Validate().Should().NotBeEmpty();    // must be >= 1
        new RiskOptions { BaseRiskPercent = 5m, HardMaxRiskPercent = 4.5m }.Validate().Should().NotBeEmpty(); // max < base
        new RiskOptions { HardMaxRiskPercent = 5m }.Validate().Should().NotBeEmpty();   // above the §2.5.5 4.5% hard ceiling
    }

    [Fact]
    public void A_target_equilibrium_fraction_outside_the_open_unit_interval_is_rejected()
    {
        new TargetLadderOptions { T1EquilibriumFraction = 0m }.Validate().Should().NotBeEmpty();
        new TargetLadderOptions { T1EquilibriumFraction = 1m }.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void A_non_positive_assembly_window_is_rejected()
        => new SetupCandidateOptions { MaxAssemblyBars = 0 }.Validate().Should().NotBeEmpty();

    [Fact]
    public void A_killzone_entry_set_outside_the_frozen_contract_is_rejected()
        => new KillzoneEntryOptions { ActiveKillzones = [Killzone.Pm] }.Validate().Should().NotBeEmpty();

    [Fact]
    public void A_negative_draw_stop_buffer_is_rejected()
        => new DrawOnLiquidityOptions { StopBufferPips = -1m }.Validate().Should().NotBeEmpty();

    [Fact]
    public void An_out_of_range_alert_minimum_grade_is_rejected()
        => new ConfluenceOptions { AlertMinimumGrade = (SetupGrade)99 }.Validate().Should().NotBeEmpty();

    [Theory]
    [InlineData(-1, 3)] // negative pip floor
    [InlineData(0, 0)]  // non-positive leg window
    public void Displacement_gates_must_be_in_range(int minPips, int legMaxBars)
        => new DisplacementOptions { MinDisplacementPips = minPips, DisplacementLegMaxBars = legMaxBars }
            .Validate().Should().NotBeEmpty();

    [Fact]
    public void An_undefined_leg_anchor_mode_is_rejected()
        => new DisplacementOptions { AnchorMode = (LegAnchorMode)99 }.Validate().Should().NotBeEmpty();

    [Fact]
    public void Fvg_atr_and_proximity_settings_must_be_in_range()
    {
        new FvgOptions { AtrPeriod = 0 }.Validate().Should().NotBeEmpty();
        new FvgOptions { AtrMultiple = -1m }.Validate().Should().NotBeEmpty();
        new FvgOptions { StackProximityPips = -1m }.Validate().Should().NotBeEmpty();
        new FvgOptions { TouchSemantics = (FvgTouchSemantics)99 }.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void The_strict_first_fvg_flag_defaults_off_and_validates_clean()
    {
        // FVG-SEM-2a: the strict-first-FVG selection is an additive flag (a bool needs no Validate rule); the
        // default OFF keeps the nearest-sweet-spot path, so the default set must stay clean.
        new FvgOptions().StrictFirstFvg.Should().BeFalse();
        new FvgOptions { StrictFirstFvg = true }.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Active_killzones_must_be_a_subset_of_the_frozen_contract()
        => new KillzoneEntryOptions { ActiveKillzones = [Killzone.Pm] }.Validate().Should().NotBeEmpty();

    [Fact]
    public void Asian_is_a_selectable_low_priority_entry_killzone()
    {
        // FVG-SEM-3 (Ep10): Asian is selectable (in the frozen subset) but deprioritized = NOT in the default
        // resolved hunt-set. Enabling [Asian] validates clean and resolves to exactly Asian on the entry set.
        MarketContextOptions.SelectableKillzones.Should().Contain(Killzone.Asian);
        new KillzoneEntryOptions().ResolvedActiveKillzones.Should().NotContain(Killzone.Asian); // deprioritized by default
        new KillzoneEntryOptions { ActiveKillzones = [Killzone.Asian] }.Validate().Should().BeEmpty();
    }

    [Fact]
    public void The_fvg_validity_exclusions_flag_defaults_off_and_validates_clean()
    {
        // FVG-SEM-3: the validity-exclusion veto is an additive flag (a bool needs no Validate rule); the
        // default OFF keeps the gap selection byte-identical, so the default set must stay clean.
        new FvgOptions().ApplyValidityExclusions.Should().BeFalse();
        new FvgOptions { ApplyValidityExclusions = true }.Validate().Should().BeEmpty();
    }

    [Fact]
    public void A_macro_reference_open_time_outside_the_pre_lunch_band_is_rejected()
    {
        // 00:00 collides with the midnight open; >= noon is past the macro window (TIME-10).
        new MarketContextOptions { MacroReferenceOpenTime = TimeOnly.MinValue }.Validate().Should().NotBeEmpty();
        new MarketContextOptions { MacroReferenceOpenTime = new TimeOnly(12, 0) }.Validate().Should().NotBeEmpty();
        new MarketContextOptions { MacroReferenceOpenTime = new TimeOnly(13, 0) }.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void A_non_positive_cluster_cap_is_rejected()
        => new OrderBlockOptions { MaxClusterCandles = 0 }.Validate().Should().NotBeEmpty();

    [Fact]
    public void A_reward_floor_below_the_hard_two_to_one_is_rejected()
        => new TradeStyleOptions { AbsoluteMinRewardRatio = 1.5m }.Validate().Should().NotBeEmpty();

    [Fact]
    public void Resolving_an_unknown_trade_style_throws()
    {
        var act = () => new TradeStyleOptions().For((TradeStyle)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Moving_the_equilibrium_boundary_off_the_ict_fifty_percent_is_rejected()
    {
        // The premium/discount boundary is a semantic invariant, not a tuning knob.
        new DailyBiasOptions { EquilibriumPercent = 0.40m }.Validate().Should().NotBeEmpty();
        new PremiumDiscountOptions { EquilibriumPercent = 0.55m }.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void Resolved_active_styles_defaults_to_intraday_when_unconfigured()
    {
        new MarketContextOptions().ResolvedActiveStyles.Should().Equal(TradeStyle.Intraday);
    }

    [Fact]
    public void Resolved_active_styles_deduplicates_to_avoid_double_feeding_a_scanner()
    {
        // A duplicate style (e.g. from the config binder appending onto the default) must collapse to one — else
        // the candle handler would feed every candle to the same per-(symbol, style) scanner more than once.
        var options = new MarketContextOptions { ActiveStyles = [TradeStyle.Intraday, TradeStyle.Intraday] };

        options.ResolvedActiveStyles.Should().Equal(TradeStyle.Intraday);
    }

    [Fact]
    public void Resolved_active_killzones_defaults_to_london_and_new_york_when_unconfigured()
    {
        new KillzoneEntryOptions().ResolvedActiveKillzones
            .Should().Equal(Killzone.LondonOpen, Killzone.NewYorkOpen);
    }

    [Fact]
    public void Configured_active_killzones_replace_the_default_and_are_not_duplicated()
    {
        // REGRESSION (binder-append): an operator narrowing the hunt-set to ONLY London Open must NOT silently
        // still hunt New York. With the empty default + Resolved accessor, the configured set replaces cleanly.
        new KillzoneEntryOptions { ActiveKillzones = [Killzone.LondonOpen] }
            .ResolvedActiveKillzones.Should().Equal(Killzone.LondonOpen);
    }

    [Fact]
    public void Resolved_active_killzones_deduplicates_a_binder_appended_set()
    {
        new KillzoneEntryOptions { ActiveKillzones = [Killzone.LondonOpen, Killzone.LondonOpen] }
            .ResolvedActiveKillzones.Should().Equal(Killzone.LondonOpen);
    }

    [Fact]
    public void Resolved_loss_ladder_replaces_and_falls_back_to_the_default()
    {
        // [12] binder-append: an operator's `[0.2, 0.1]` must NOT become `[0.5, 0.25, 0.2, 0.1]`.
        new RiskOptions { LossLadderPercents = [0.2m, 0.1m] }.ResolvedLossLadderPercents
            .Should().Equal(0.2m, 0.1m);
        new RiskOptions().ResolvedLossLadderPercents.Should().Equal(0.5m, 0.25m); // unconfigured = §2.5.5 default
    }

    [Fact]
    public void Resolved_sd_multiples_and_coefficients_replace_and_fall_back_to_the_default()
    {
        // [14] binder-append on Multiples + the nested NegativeFibOptions.Coefficients.
        new SdProjectionOptions { Multiples = [3.0m, 4.0m] }.ResolvedMultiples.Should().Equal(3.0m, 4.0m);
        new SdProjectionOptions().ResolvedMultiples.Should().Equal(1.0m, 1.5m, 2.0m);
        new NegativeFibOptions { Coefficients = [0.5m] }.ResolvedCoefficients.Should().Equal(0.5m);
        new NegativeFibOptions().ResolvedCoefficients.Should().Equal(0.27m, 0.62m, 1.0m);
    }

    [Fact]
    public void Resolved_standing_conditions_honor_an_operator_removal()
    {
        // [15] binder-append: removing PremiumDiscountHalf (moving it to event-latching) must be honored, not
        // silently re-added by a prepended default.
        var configured = new[]
        {
            ConfluenceCondition.BiasAligned, ConfluenceCondition.KillzoneEntry, ConfluenceCondition.CalendarClear,
        };

        var resolved = new SetupCandidateOptions { StandingConditions = configured }.ResolvedStandingConditions;

        resolved.Should().NotContain(ConfluenceCondition.PremiumDiscountHalf);
        resolved.Should().BeEquivalentTo(configured);
        new SetupCandidateOptions().ResolvedStandingConditions
            .Should().BeEquivalentTo(SetupCandidateOptions.DefaultStandingConditions); // unconfigured = §2.5 default
    }

    [Fact]
    public void Effective_required_conditions_replace_and_fall_back_to_the_default()
    {
        // [22] binder-append: an operator dropping CalendarClear must be honored, not re-appended.
        var configured = new[] { ConfluenceCondition.KillzoneEntry, ConfluenceCondition.LiquiditySweep };

        new ConfluenceOptions { RequiredConditions = configured }.EffectiveRequiredConditions
            .Should().BeEquivalentTo(configured);
        new ConfluenceOptions().EffectiveRequiredConditions
            .Should().BeEquivalentTo(ConfluenceOptions.DefaultRequiredConditions);
    }

    [Fact]
    public void An_undefined_entry_mode_is_rejected()
        => new EntryManagementOptions { Mode = (EntryMode)99 }.Validate().Should().NotBeEmpty();
}
