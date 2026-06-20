using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;

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
        new MarketContextOptions().Validate().Should().BeEmpty();
        new TradeStyleOptions().Validate().Should().BeEmpty();
        new SetupCandidateOptions().Validate().Should().BeEmpty();
    }

    [Fact]
    public void A_non_positive_assembly_window_is_rejected()
        => new SetupCandidateOptions { MaxAssemblyBars = 0 }.Validate().Should().NotBeEmpty();

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
    public void Fvg_atr_and_proximity_settings_must_be_in_range()
    {
        new FvgOptions { AtrPeriod = 0 }.Validate().Should().NotBeEmpty();
        new FvgOptions { AtrMultiple = -1m }.Validate().Should().NotBeEmpty();
        new FvgOptions { StackProximityPips = -1m }.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void Active_killzones_must_be_a_subset_of_the_frozen_contract()
        => new MarketContextOptions { ActiveKillzones = [Killzone.Pm] }.Validate().Should().NotBeEmpty();

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
}
