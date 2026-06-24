using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Setups;

namespace IctTrader.UnitTests.Confluence;

/// <summary>
/// Locks the §2.5.4 grading: score = Σ(matched)/Σ(applicable) ×100, with any missing RequiredCondition
/// forcing Reject regardless of score, and every weight/threshold sourced from <see cref="ConfluenceOptions"/>.
/// </summary>
public class SetupScorerTests
{
    // Ten equally-weighted conditions so matched-count maps directly to a 0..100 score; CalendarClear is
    // an unweighted hard-gate RequiredCondition (must be matched but contributes nothing to the score).
    private static readonly ConfluenceCondition[] Weighted =
    [
        ConfluenceCondition.KillzoneEntry, ConfluenceCondition.LiquiditySweep, ConfluenceCondition.DisplacementMss,
        ConfluenceCondition.FvgPresent, ConfluenceCondition.BiasAligned, ConfluenceCondition.PremiumDiscountHalf,
        ConfluenceCondition.OteZone, ConfluenceCondition.OrderBlockConfluence, ConfluenceCondition.DrawTargetRrMet,
        ConfluenceCondition.SmtDivergence,
    ];

    private static SetupScorer ScorerWithUnitWeights() => new(new ConfluenceOptions
    {
        Weights = Weighted.ToDictionary(c => c, _ => 1.0m),
        RequiredConditions = [ConfluenceCondition.KillzoneEntry, ConfluenceCondition.CalendarClear],
    });

    private static IReadOnlySet<ConfluenceCondition> Applicable()
        => new HashSet<ConfluenceCondition>(Weighted) { ConfluenceCondition.CalendarClear };

    private static IReadOnlySet<ConfluenceCondition> MatchFirst(int weightedCount, bool includeCalendarClear)
    {
        var set = new HashSet<ConfluenceCondition>(Weighted.Take(weightedCount));
        if (includeCalendarClear)
        {
            set.Add(ConfluenceCondition.CalendarClear);
        }

        return set;
    }

    [Theory]
    [InlineData(8, 80, SetupGrade.A)]   // at/above the A threshold
    [InlineData(7, 70, SetupGrade.B)]   // below A but all-required ⇒ B
    [InlineData(5, 50, SetupGrade.B)]   // a low score no longer demotes a complete setup to C...
    [InlineData(4, 40, SetupGrade.B)]   // ...nor to Reject — all-required is always at least a tradeable B (TGR-4)
    public void An_all_required_setup_grades_A_above_the_threshold_else_B(
        int matchedWeighted, int expectedScore, SetupGrade expectedGrade)
    {
        var result = ScorerWithUnitWeights().Score(MatchFirst(matchedWeighted, includeCalendarClear: true), Applicable());

        result.Score.Should().Be(expectedScore);   // the raw 0–100 score is unchanged — it is the within-grade sorter
        result.Grade.Should().Be(expectedGrade);
        result.AllRequiredMatched.Should().BeTrue();
    }

    [Fact]
    public void A_bare_all_required_setup_scores_63_and_grades_a_tradeable_B()
    {
        // The §2.5 model with ALL RequiredConditions and ZERO optional confluences, scored with the default §2.5.3
        // weights. The raw score is LOCKED at 6.15/9.75 = 63 so a future weight change can't silently move the alert
        // boundary; all-required auto-clears to a tradeable B (TGR-4) rather than the suppressed C the bare 63 implies.
        var options = new ConfluenceOptions();
        var scorer = new SetupScorer(options);
        var applicable = options.Weights.Keys.ToHashSet();   // the constant weighted universe (Σ = 9.75)
        var matched = options.RequiredConditions.ToHashSet(); // all required, zero optional

        var result = scorer.Score(matched, applicable);

        result.AllRequiredMatched.Should().BeTrue();
        result.Score.Should().Be(63);                        // 6.15 / 9.75 × 100
        result.Grade.Should().Be(SetupGrade.B);              // tradeable, not the suppressed Grade C
        result.MeetsAlertFloor(options.AlertMinimumGrade).Should().BeTrue();
    }

    [Fact]
    public void The_display_only_b_threshold_does_not_gate_the_grade()
    {
        // Since TGR-4, GradeBThreshold is a display-band label, not a grading gate: an all-required setup scoring 50
        // grades B even with the B threshold pushed up to 79 — under the retired floor it would have been suppressed.
        var scorer = new SetupScorer(new ConfluenceOptions
        {
            Weights = Weighted.ToDictionary(c => c, _ => 1.0m),
            RequiredConditions = [ConfluenceCondition.KillzoneEntry, ConfluenceCondition.CalendarClear],
            GradeBThreshold = 79,
            GradeCThreshold = 60,
        });

        var result = scorer.Score(MatchFirst(5, includeCalendarClear: true), Applicable()); // score 50, all required

        result.Score.Should().Be(50);
        result.Grade.Should().Be(SetupGrade.B); // the display threshold cannot demote a complete setup
    }

    [Fact]
    public void Rejects_when_a_required_condition_is_missing_even_at_a_perfect_score()
    {
        // 8/10 weighted matched (score 80) but the required hard gate CalendarClear is absent.
        var result = ScorerWithUnitWeights().Score(MatchFirst(8, includeCalendarClear: false), Applicable());

        result.Score.Should().Be(80);
        result.AllRequiredMatched.Should().BeFalse();
        result.Grade.Should().Be(SetupGrade.Reject);
    }

    [Fact]
    public void Alert_floor_lets_b_through_but_not_c()
    {
        var b = new ConfluenceScore(70, SetupGrade.B, true);
        var c = new ConfluenceScore(55, SetupGrade.C, true);

        b.MeetsAlertFloor(SetupGrade.B).Should().BeTrue();
        c.MeetsAlertFloor(SetupGrade.B).Should().BeFalse();
    }

    [Fact]
    public void Default_weights_match_the_mined_2_5_3_values()
    {
        ConfluenceOptions.DefaultWeights[ConfluenceCondition.KillzoneEntry].Should().Be(1.00m);
        ConfluenceOptions.DefaultWeights[ConfluenceCondition.LiquiditySweep].Should().Be(0.95m);
        ConfluenceOptions.DefaultWeights[ConfluenceCondition.DisplacementMss].Should().Be(0.95m);
        ConfluenceOptions.DefaultWeights[ConfluenceCondition.FvgPresent].Should().Be(0.90m);
        ConfluenceOptions.DefaultWeights.Should().NotContainKey(ConfluenceCondition.CalendarClear);
    }

    [Fact]
    public void Default_configuration_validates_clean_but_descending_thresholds_are_enforced()
    {
        new ConfluenceOptions().Validate().Should().BeEmpty();

        new ConfluenceOptions { GradeBThreshold = 90 }.Validate().Should().NotBeEmpty();
    }
}
