using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Setups;

/// <summary>
/// Locks the priced-plan invariants (plan §2.5.1 steps 7–9, §3.0): the prices run strictly
/// stop → entry → T1 → T2 in the trade direction, the reward-to-risk is computed entry→runner, and a swapped
/// stop or target can never form a plan.
/// </summary>
public class TradePlanTests
{
    private static TargetLadder BullLadder() => new(Direction.Bullish, new Price(1.0866m), new Price(1.0920m));

    [Fact]
    public void A_bullish_plan_orders_the_prices_and_computes_reward_to_risk()
    {
        var plan = new TradePlan(Direction.Bullish, new Price(1.0832m), new Price(1.0800m), BullLadder());

        plan.RewardRatio.Value.Should().BeApproximately(2.75m, 0.0001m); // 88 pips reward / 32 pips risk
        plan.Targets.Partial.Value.Should().Be(1.0866m);
        plan.Targets.Runner.Value.Should().Be(1.0920m);
    }

    [Fact]
    public void A_bearish_plan_mirrors_the_ordering()
    {
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0830m), new Price(1.0790m)));

        plan.Direction.Should().Be(Direction.Bearish);
        plan.RewardRatio.Value.Should().BeApproximately(2.6667m, 0.001m); // 80 pips / 30 pips
    }

    [Fact]
    public void A_stop_on_the_wrong_side_of_entry_is_rejected()
    {
        var act = () => new TradePlan(Direction.Bullish, new Price(1.0832m), new Price(1.0850m), BullLadder());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_target_ladder_not_beyond_entry_is_rejected()
    {
        // T1/T2 below the entry for a long -> the price order breaks.
        var act = () => new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0820m), new Price(1.0828m)));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_ladder_with_the_partial_past_the_runner_is_rejected()
    {
        var act = () => new TargetLadder(Direction.Bullish, new Price(1.0930m), new Price(1.0920m));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void An_n_tier_ladder_names_the_runner_and_keeps_deeper_targets_beyond_it()
    {
        // TGR-1/2: T1, T2(runner/draw), then deeper SD tiers. The runner is the NAMED tier, not the deepest.
        var ladder = new TargetLadder(
            Direction.Bullish,
            new[] { new Price(1.0866m), new Price(1.0920m), new Price(1.0960m), new Price(1.1000m) },
            runnerIndex: 1);

        ladder.Partial.Value.Should().Be(1.0866m);
        ladder.Runner.Value.Should().Be(1.0920m); // index 1 — the gated draw, NOT the 1.1000 deepest tier
        ladder.TierCount.Should().Be(4);
        ladder.Targets.Select(t => t.Value).Should().Equal(1.0866m, 1.0920m, 1.0960m, 1.1000m);
    }

    [Fact]
    public void A_plan_with_deeper_targets_measures_reward_to_risk_to_the_runner_not_the_deepest_tier()
    {
        var ladder = new TargetLadder(
            Direction.Bullish,
            new[] { new Price(1.0866m), new Price(1.0920m), new Price(1.1000m) },
            runnerIndex: 1);
        var plan = new TradePlan(Direction.Bullish, new Price(1.0832m), new Price(1.0800m), ladder);

        plan.RewardRatio.Value.Should().BeApproximately(2.75m, 0.0001m); // entry->runner(1.0920), NOT entry->1.1000
    }

    [Fact]
    public void A_non_monotone_n_tier_ladder_is_rejected()
    {
        var act = () => new TargetLadder(
            Direction.Bullish,
            new[] { new Price(1.0866m), new Price(1.0920m), new Price(1.0900m) }, // 1.0900 < 1.0920 breaks the order
            runnerIndex: 1);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_runner_index_outside_the_ladder_is_rejected()
    {
        var act = () => new TargetLadder(
            Direction.Bullish, new[] { new Price(1.0866m), new Price(1.0920m) }, runnerIndex: 5);

        act.Should().Throw<DomainException>();
    }
}
