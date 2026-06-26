using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.PaperTrading.Application;
using IctTrader.Scanning.Contracts;

namespace IctTrader.UnitTests.PaperTrading;

/// <summary>
/// Locks the consumer half of the scan→trade seam (Architecture A): `SetupRehydrator` rebuilds a faithful
/// domain <see cref="Setup"/> from the wire <see cref="SetupDto"/> — the geometry (entry/stop/targets/RR)
/// round-trips byte-identically (the plan recomputes RR from the same numbers), the within-grade score rebuilds
/// as the grade's configured floor, and the detection time is normalised to UTC.
/// </summary>
public class SetupRehydratorTests
{
    private static readonly ConfluenceOptions Grading = new();

    private static SetupDto BullishDto() => new(
        Id: Guid.NewGuid(),
        Symbol: "EURUSD",
        Direction: Direction.Bullish.ToString(),
        Killzone: Killzone.LondonOpen.ToString(),
        Style: TradeStyle.Intraday.ToString(),
        Grade: SetupGrade.B.ToString(),
        TriggerTimeframe: Timeframe.M5.ToString(),
        Entry: 1.0832m,
        Stop: 1.0810m,                         // risk 0.0022
        Targets: [1.0876m, 1.0920m],           // T1 partial, runner (reward 0.0088 → RR 4.0)
        RewardRatio: 4.0m,
        Reason: "Bullish FVG in London Open after Asian-low sweep; MSS confirmed; OTE 0.705.",
        DetectedAtUtc: new DateTimeOffset(2024, 7, 1, 6, 0, 0, TimeSpan.Zero),
        IsAdvisoryOnly: true);

    [Fact]
    public void Rehydrates_a_faithful_setup_from_the_dto()
    {
        var dto = BullishDto();

        var setup = SetupRehydrator.ToDomain(dto, Grading);

        setup.Symbol.Value.Should().Be("EURUSD");
        setup.Direction.Should().Be(Direction.Bullish);
        setup.Style.Should().Be(TradeStyle.Intraday);
        setup.Timeframe.Should().Be(Timeframe.M5);
        setup.Grade.Should().Be(SetupGrade.B);
        setup.IsAdvisoryOnly.Should().BeTrue();
        setup.StackedFartherBound.Should().BeNull();

        setup.Plan.Entry.Value.Should().Be(1.0832m);
        setup.Plan.Stop.Value.Should().Be(1.0810m);
        setup.Plan.Targets.Partial.Value.Should().Be(1.0876m);
        setup.Plan.Targets.Runner.Value.Should().Be(1.0920m);
        setup.Plan.Targets.TierCount.Should().Be(2);
        setup.Plan.Targets.RunnerIndex.Should().Be(1);
        setup.Reason.Text.Should().Be(dto.Reason);
        setup.ConfirmedAtUtc.Should().Be(dto.DetectedAtUtc);
    }

    [Fact]
    public void Recomputes_reward_to_risk_from_the_geometry_not_the_wire_value()
    {
        // A deliberately wrong wire RR must NOT be trusted — the plan recomputes it (reward 0.0088 / risk 0.0022).
        var dto = BullishDto() with { RewardRatio = 99m };

        var setup = SetupRehydrator.ToDomain(dto, Grading);

        setup.Plan.RewardRatio.Value.Should().Be(4.0m);
    }

    [Theory]
    [InlineData("A", 80)]   // default GradeAThreshold
    [InlineData("B", 65)]   // default GradeBThreshold
    public void Rebuilds_the_score_as_the_grade_floor(string grade, int expectedScore)
    {
        var dto = BullishDto() with { Grade = grade };

        var setup = SetupRehydrator.ToDomain(dto, Grading);

        setup.Score.Should().Be(expectedScore);
    }

    [Fact]
    public void Normalises_the_detection_time_to_utc()
    {
        // 08:00 at +02:00 is 06:00 UTC — the Setup requires a zero-offset (UTC) confirmation time.
        var dto = BullishDto() with { DetectedAtUtc = new DateTimeOffset(2024, 7, 1, 8, 0, 0, TimeSpan.FromHours(2)) };

        var setup = SetupRehydrator.ToDomain(dto, Grading);

        setup.ConfirmedAtUtc.Should().Be(new DateTimeOffset(2024, 7, 1, 6, 0, 0, TimeSpan.Zero));
        setup.ConfirmedAtUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Rehydrates_a_short_setup_with_mirrored_geometry()
    {
        var dto = BullishDto() with
        {
            Direction = Direction.Bearish.ToString(),
            Entry = 1.0832m,
            Stop = 1.0850m,                    // stop ABOVE entry for a short
            Targets = [1.0800m, 1.0760m],      // descending, below entry
        };

        var setup = SetupRehydrator.ToDomain(dto, Grading);

        setup.Direction.Should().Be(Direction.Bearish);
        setup.Plan.Stop.Value.Should().BeGreaterThan(setup.Plan.Entry.Value);
        setup.Plan.Targets.Runner.Value.Should().Be(1.0760m);
        setup.Plan.RewardRatio.Value.Should().Be(4.0m);   // (1.0832-1.0760)/(1.0850-1.0832)
    }

    [Fact]
    public void Rehydrates_a_three_tier_ladder_with_the_runner_pinned_to_the_canonical_tier()
    {
        // A setup carrying an SD extension beyond the runner (Targets[2]). The RR must STILL be measured to the
        // canonical runner tier (index 1 = the gated draw), NOT the deepest target — so enabling SD on the
        // producer can never inflate the rehydrated RR. Pins the producer/consumer runner-index convention.
        var dto = BullishDto() with { Targets = [1.0876m, 1.0920m, 1.0980m] };

        var setup = SetupRehydrator.ToDomain(dto, Grading);

        setup.Plan.Targets.TierCount.Should().Be(3);
        setup.Plan.Targets.RunnerIndex.Should().Be(TargetLadder.CanonicalRunnerIndex);
        setup.Plan.Targets.Runner.Value.Should().Be(1.0920m);   // the gated draw, not the deepest SD tier
        setup.Plan.RewardRatio.Value.Should().Be(4.0m);          // (1.0920-1.0832)/(1.0832-1.0810), to the runner
    }

    [Fact]
    public void Rejects_an_unknown_enum_member()
    {
        var dto = BullishDto() with { Direction = "Sideways" };

        var rehydrate = () => SetupRehydrator.ToDomain(dto, Grading);

        rehydrate.Should().Throw<FormatException>().WithMessage("*Sideways*");
    }
}
