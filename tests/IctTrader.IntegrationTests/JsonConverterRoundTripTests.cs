using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.PaperTrading.Infrastructure.Persistence;

namespace IctTrader.IntegrationTests;

/// <summary>
/// PURE (Docker-free) round-trip tests for the JSONB value converters (plan §7). They drive the converter
/// delegates directly (no Postgres), proving the serialize→deserialize fidelity that DB-as-state relies on —
/// in particular that an N-tier <see cref="TargetLadder"/> (the TGR-1/2 standard-deviation projection tiers
/// beyond the runner) survives the round-trip. A 2-target JSON shape previously dropped tiers[2..], so a
/// 3+-tier position written this bar came back 2-tier next bar under DB-as-state.
/// </summary>
public sealed class JsonConverterRoundTripTests
{
    private static readonly DateTimeOffset Confirmed = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    // Long: stop 1.0800 < entry 1.0832 < T1 1.0876 < runner 1.0920 < SD1 1.0960 < SD2 1.1000 — a 4-tier ladder,
    // runner at the canonical index 1 (the deeper tiers are advisory SD projections that must not move the RR).
    private static TargetLadder FourTierLadder() => new(
        Direction.Bullish,
        [new Price(1.0876m), new Price(1.0920m), new Price(1.0960m), new Price(1.1000m)],
        TargetLadder.CanonicalRunnerIndex);

    private static TradePlan FourTierPlan() => new(
        Direction.Bullish, new Price(1.0832m), new Price(1.0800m), FourTierLadder());

    [Fact]
    public void TradePlan_jsonb_round_trip_preserves_every_target_tier_and_the_runner_index()
    {
        var plan = FourTierPlan();

        var json = (string)JsonConverters.TradePlanConverter.ConvertToProvider(plan)!;
        var restored = (TradePlan)JsonConverters.TradePlanConverter.ConvertFromProvider(json)!;

        restored.Targets.TierCount.Should().Be(4, "all four target tiers must survive the round-trip");
        restored.Targets.Targets.Select(t => t.Value)
            .Should().Equal(1.0876m, 1.0920m, 1.0960m, 1.1000m);
        restored.Targets.RunnerIndex.Should().Be(TargetLadder.CanonicalRunnerIndex);
        restored.Targets.Runner.Value.Should().Be(1.0920m); // RR is still measured to the gated draw, not the deepest tier
        restored.RewardRatio.Value.Should().Be(plan.RewardRatio.Value); // RR unchanged (recomputed entry→runner)
        restored.Entry.Value.Should().Be(1.0832m);
        restored.Stop.Value.Should().Be(1.0800m);
    }

    [Fact]
    public void Setup_jsonb_round_trip_preserves_every_target_tier_and_the_runner_index()
    {
        var setup = new Setup(
            new Symbol("EURUSD"),
            TradeStyle.Intraday,
            Timeframe.M5,
            SetupGrade.B,
            score: 70,
            FourTierPlan(),
            new SetupReason("bias; sweep; MSS; FVG; OTE; SD targets"),
            Confirmed);

        var json = (string)JsonConverters.SetupConverter.ConvertToProvider(setup)!;
        var restored = (Setup)JsonConverters.SetupConverter.ConvertFromProvider(json)!;

        restored.Plan.Targets.TierCount.Should().Be(4);
        restored.Plan.Targets.Targets.Select(t => t.Value)
            .Should().Equal(1.0876m, 1.0920m, 1.0960m, 1.1000m);
        restored.Plan.Targets.RunnerIndex.Should().Be(TargetLadder.CanonicalRunnerIndex);
        restored.Plan.RewardRatio.Value.Should().Be(setup.Plan.RewardRatio.Value);
        restored.Grade.Should().Be(SetupGrade.B);
        restored.Score.Should().Be(70);
    }

    [Fact]
    public void TradePlan_jsonb_round_trip_is_byte_identical_for_the_default_two_tier_ladder()
    {
        // Guard the default (SD-off) path: a plain T1+runner ladder must still round-trip exactly.
        var plan = new TradePlan(
            Direction.Bullish,
            new Price(1.0832m),
            new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));

        var json = (string)JsonConverters.TradePlanConverter.ConvertToProvider(plan)!;
        var restored = (TradePlan)JsonConverters.TradePlanConverter.ConvertFromProvider(json)!;

        restored.Targets.TierCount.Should().Be(2);
        restored.Targets.Partial.Value.Should().Be(1.0876m);
        restored.Targets.Runner.Value.Should().Be(1.0920m);
        restored.RewardRatio.Value.Should().Be(plan.RewardRatio.Value);
    }
}
