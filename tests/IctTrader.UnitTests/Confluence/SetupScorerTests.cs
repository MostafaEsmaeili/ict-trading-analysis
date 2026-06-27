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
        var matched = options.EffectiveRequiredConditions.ToHashSet(); // all required, zero optional

        var result = scorer.Score(matched, applicable);

        result.AllRequiredMatched.Should().BeTrue();
        result.Score.Should().Be(63);                        // 6.15 / 9.75 × 100
        result.Grade.Should().Be(SetupGrade.B);              // tradeable, not the suppressed Grade C
        result.MeetsAlertFloor(options.AlertMinimumGrade).Should().BeTrue();
    }

    [Fact]
    public void Grade_A_is_reachable_with_the_optional_emitters_under_the_default_weights()
    {
        // PROOF that Grade A is reachable end-to-end through SetupScorer once the four optional emitters can match.
        // Under the §2.5.3 default weights: all RequiredConditions (Σ = 6.15) + OteZone (0.70) + OpenPriceReference
        // (0.50) + MacroTime (0.45) = 7.80 of the constant Σ = 9.75 universe → 80 → Grade A (the GradeAThreshold).
        var options = new ConfluenceOptions();
        var scorer = new SetupScorer(options);
        var applicable = options.Weights.Keys.ToHashSet(); // the constant weighted universe (Σ = 9.75)
        var matched = options.EffectiveRequiredConditions
            .Concat([
                ConfluenceCondition.OteZone,
                ConfluenceCondition.OpenPriceReference,
                ConfluenceCondition.MacroTime,
            ])
            .ToHashSet();

        var result = scorer.Score(matched, applicable);

        result.AllRequiredMatched.Should().BeTrue();
        result.Score.Should().Be(80);             // 7.80 / 9.75 × 100
        result.Grade.Should().Be(SetupGrade.A);   // the optional emitters PROMOTE a complete setup to A
    }

    [Fact]
    public void The_optional_emitters_are_not_required_so_the_bare_setup_still_scores_63()
    {
        // GRADING-SAFETY: the four new emitters are OPTIONAL (not in EffectiveRequiredConditions), so a bare
        // all-required setup is unchanged — still 63 (6.15 / 9.75) and a tradeable B. Adding their weights only ever
        // adds to the numerator; Σ(applicable) is untouched, so existing grades never drop.
        var options = new ConfluenceOptions();

        options.EffectiveRequiredConditions.Should().NotContain(ConfluenceCondition.OpenPriceReference);
        options.EffectiveRequiredConditions.Should().NotContain(ConfluenceCondition.MacroTime);
        options.EffectiveRequiredConditions.Should().NotContain(ConfluenceCondition.CleanPriceAction);
        options.EffectiveRequiredConditions.Should().NotContain(ConfluenceCondition.CalendarDriver);

        var result = new SetupScorer(options).Score(
            options.EffectiveRequiredConditions.ToHashSet(), options.Weights.Keys.ToHashSet());

        result.Score.Should().Be(63);
        result.Grade.Should().Be(SetupGrade.B);
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

    // ---- k-of-n required-condition relaxation (the configurable MinRequiredConditions) ----

    // The first four weighted conditions used as a 4-strong required set + the remaining six as "optional".
    private static readonly ConfluenceCondition[] FourRequired =
    [
        ConfluenceCondition.KillzoneEntry, ConfluenceCondition.LiquiditySweep,
        ConfluenceCondition.DisplacementMss, ConfluenceCondition.FvgPresent,
    ];

    private static readonly ConfluenceCondition[] SixOptional =
    [
        ConfluenceCondition.BiasAligned, ConfluenceCondition.PremiumDiscountHalf, ConfluenceCondition.OteZone,
        ConfluenceCondition.OrderBlockConfluence, ConfluenceCondition.DrawTargetRrMet, ConfluenceCondition.SmtDivergence,
    ];

    // 2 of the 4 required + all 6 optional = 8 of the 10 weighted → score 80, but an INCOMPLETE required set.
    private static IReadOnlySet<ConfluenceCondition> TwoRequiredPlusSixOptional()
        => new HashSet<ConfluenceCondition>(SixOptional)
        {
            ConfluenceCondition.KillzoneEntry,
            ConfluenceCondition.LiquiditySweep,
        };

    private static SetupScorer Scorer(int? minRequired) => new(new ConfluenceOptions
    {
        Weights = Weighted.ToDictionary(c => c, _ => 1.0m),
        RequiredConditions = FourRequired,
        MinRequiredConditions = minRequired,
    });

    [Fact]
    public void Strict_default_rejects_a_partial_required_setup_even_at_a_high_score()
    {
        // The canonical all-AND model: 2 of 4 required (+ 6 optional, score 80) is still a Reject — the strict gate
        // demands every RequiredCondition. This is the behaviour the relaxation deliberately opts out of.
        var result = Scorer(minRequired: null).Score(TwoRequiredPlusSixOptional(), new HashSet<ConfluenceCondition>(Weighted));

        result.Score.Should().Be(80);
        result.AllRequiredMatched.Should().BeFalse();
        result.Grade.Should().Be(SetupGrade.Reject);
    }

    [Fact]
    public void Relaxed_k_of_n_confirms_a_partial_required_setup_graded_by_its_score()
    {
        // With MinRequiredConditions = 2, the same 2-of-4 setup passes the relaxed gate and is graded purely by its
        // weighted score (80 ≥ the A threshold) — the user's "k of n" idea: trade more, but only when the confluence
        // score is genuinely high. It is still flagged as NOT a complete §2.5 setup.
        var result = Scorer(minRequired: 2).Score(TwoRequiredPlusSixOptional(), new HashSet<ConfluenceCondition>(Weighted));

        result.Score.Should().Be(80);
        result.AllRequiredMatched.Should().BeFalse();
        result.Grade.Should().Be(SetupGrade.A);
    }

    [Fact]
    public void Relaxed_still_rejects_below_the_k_threshold()
    {
        // Only 1 required (+ 6 optional, score 70) is below the k = 2 threshold → Reject even though the score clears
        // the alert floor: the relaxation lowers the bar, it does not remove it.
        var matched = new HashSet<ConfluenceCondition>(SixOptional) { ConfluenceCondition.KillzoneEntry };

        var result = Scorer(minRequired: 2).Score(matched, new HashSet<ConfluenceCondition>(Weighted));

        result.Score.Should().Be(70);
        result.Grade.Should().Be(SetupGrade.Reject);
    }

    [Fact]
    public void MinRequiredConditions_must_be_within_the_required_set_size()
    {
        new ConfluenceOptions { MinRequiredConditions = 0 }.Validate().Should().NotBeEmpty();
        new ConfluenceOptions { MinRequiredConditions = 99 }.Validate().Should().NotBeEmpty();
        new ConfluenceOptions { MinRequiredConditions = 5 }.Validate().Should().BeEmpty(); // 5 ≤ 8 default required
    }
}
