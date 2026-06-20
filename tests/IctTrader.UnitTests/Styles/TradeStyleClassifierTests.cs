using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Styles;

/// <summary>
/// Locks trade-style resolution + classification (plan §4.7): the per-style timeframe triple, the
/// hold-band classification, the 2:1 RR floor, the strictly-descending timeframe validation, and the
/// provenance-driven default that Scalp does NOT take a direct-FVG entry.
/// </summary>
public class TradeStyleClassifierTests
{
    private static readonly TradeStyleClassifier Classifier = new(new TradeStyleOptions());

    [Fact]
    public void Resolves_the_intraday_timeframe_triple()
    {
        var policy = Classifier.ResolvePolicy(TradeStyle.Intraday);

        policy.BiasTimeframe.Should().Be(Timeframe.D1);
        policy.StructureTimeframe.Should().Be(Timeframe.M15);
        policy.EntryTimeframe.Should().Be(Timeframe.M5);
        policy.MaxHold.Should().Be(TimeSpan.FromMinutes(120));
        policy.MinRewardRatio.Value.Should().Be(2.5m);
    }

    [Theory]
    [InlineData(20, TradeStyle.Scalp)]
    [InlineData(90, TradeStyle.Intraday)]
    [InlineData(7200, TradeStyle.Swing)]     // 5 days
    [InlineData(28800, TradeStyle.Position)] // 20 days
    public void Classifies_by_expected_hold(int minutes, TradeStyle expected)
        => Classifier.ClassifyByHold(TimeSpan.FromMinutes(minutes)).Should().Be(expected);

    [Fact]
    public void Scalp_does_not_take_a_direct_fvg_entry_by_default()
        => Classifier.AllowsDirectFvgEntry(TradeStyle.Scalp).Should().BeFalse();

    [Fact]
    public void Default_configuration_validates_clean()
        => new TradeStyleOptions().Validate().Should().BeEmpty();

    [Fact]
    public void Non_descending_timeframes_are_rejected()
    {
        var options = new TradeStyleOptions
        {
            Scalp = new StyleSettings
            {
                BiasTimeframe = Timeframe.M1,
                StructureTimeframe = Timeframe.M5,
                EntryTimeframe = Timeframe.H1,
                MaxHoldMinutes = 30,
                AllowOvernight = false,
                MinRewardRatio = 2.5m,
            },
        };

        options.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void A_reward_ratio_below_the_two_to_one_floor_is_rejected()
    {
        var options = new TradeStyleOptions
        {
            Intraday = new StyleSettings
            {
                BiasTimeframe = Timeframe.D1,
                StructureTimeframe = Timeframe.M15,
                EntryTimeframe = Timeframe.M5,
                MaxHoldMinutes = 120,
                AllowOvernight = false,
                MinRewardRatio = 1.5m,
            },
        };

        options.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void A_non_positive_expected_hold_is_rejected()
    {
        var act = () => Classifier.ClassifyByHold(TimeSpan.Zero);

        act.Should().Throw<DomainException>();
    }
}
