using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Setups;

/// <summary>
/// Locks the priced-Setup assembly (plan §3.0/§4.5): the factory prices T1 at the entry→T2 equilibrium and T2
/// at the EXACT captured draw, re-checks the reward-to-risk floor, and only an A/B grade becomes a Setup. The
/// Setup is structurally advisory-only.
/// </summary>
public class SetupFactoryTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Utc = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly SetupFactory Factory = new(new TargetLadderOptions(), new TradeStyleOptions());

    private static SetupConfirmation Confirmation(PricedFrame? frame, SetupGrade grade = SetupGrade.B) =>
        new(Eurusd, Direction.Bullish, Timeframe.M5, grade, 70, Utc,
            [new ConfluenceContribution(ConfluenceCondition.BiasAligned, Direction.Bullish, 1.0850m, "Daily bias Bullish")],
            frame);

    private static PricedFrame BullishFrame() => new(Direction.Bullish, 1.0832m, 1.0800m, 1.0920m, 2.75m);

    [Fact]
    public void Prices_the_setup_with_t1_at_the_leg_equilibrium_and_t2_at_the_draw()
    {
        var setup = Factory.Create(Confirmation(BullishFrame()), TradeStyle.Intraday);

        setup.Direction.Should().Be(Direction.Bullish);
        setup.Plan.Entry.Value.Should().Be(1.0832m);
        setup.Plan.Stop.Value.Should().Be(1.0800m);
        setup.Plan.Targets.Runner.Value.Should().Be(1.0920m);   // T2 = the captured draw, never re-derived
        setup.Plan.Targets.Partial.Value.Should().Be(1.0876m);  // T1 = midpoint(1.0832, 1.0920)
        setup.Plan.RewardRatio.Value.Should().BeApproximately(2.75m, 0.0001m);
        setup.Grade.Should().Be(SetupGrade.B);
        setup.Style.Should().Be(TradeStyle.Intraday);
        setup.IsAdvisoryOnly.Should().BeTrue();
        setup.Reason.Text.Should().Contain("Daily bias Bullish").And.Contain("T2 1.0920");
    }

    [Fact]
    public void A_confirmation_without_a_priced_frame_cannot_be_priced()
    {
        var act = () => Factory.Create(Confirmation(frame: null), TradeStyle.Intraday);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_reward_to_risk_below_the_style_floor_is_rejected()
    {
        // draw 1.0860 -> reward 28 pips / risk 32 pips = 0.875R, below the 2.5 Intraday floor.
        var act = () => Factory.Create(
            Confirmation(new PricedFrame(Direction.Bullish, 1.0832m, 1.0800m, 1.0860m, 0.875m)), TradeStyle.Intraday);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_watchlist_grade_never_becomes_a_setup()
    {
        var act = () => Factory.Create(Confirmation(BullishFrame(), grade: SetupGrade.C), TradeStyle.Intraday);

        act.Should().Throw<DomainException>();
    }
}
