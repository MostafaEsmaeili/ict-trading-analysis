using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Setups;

namespace IctTrader.Domain.Confluence;

/// <summary>The graded outcome of scoring a candidate's confluences (plan §2.5.4).</summary>
public readonly record struct ConfluenceScore(int Score, SetupGrade Grade, bool AllRequiredMatched)
{
    /// <summary>Whether this setup grades at or above the configured alert floor.</summary>
    public bool MeetsAlertFloor(SetupGrade floor) => Grade >= floor;
}

/// <summary>
/// Pure domain service that turns matched confluences into a 0–100 score and a grade (plan §2.5.4 / core-model
/// decisions register TGR-4): <c>score = Σ(matched weights)/Σ(applicable weights) ×100</c>. Any missing
/// RequiredCondition ⇒ Reject. An all-RequiredConditions-clean setup IS the tradeable §2.5 model, so it grades at
/// LEAST B; the weighted score is the within-grade sorter that promotes it to A at
/// <see cref="ConfluenceOptions.GradeAThreshold"/> — it is NOT a floor that can demote a complete setup to C/Reject.
/// (Consequence: A needs optional confluences, so it is unreachable until the optional emitters ship.) All
/// thresholds/weights come from <see cref="ConfluenceOptions"/> — no magic numbers, fully operator-tunable.
/// </summary>
public sealed class SetupScorer
{
    private readonly ConfluenceOptions _options;

    public SetupScorer(ConfluenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Scores a candidate. <paramref name="applicable"/> are the conditions that apply to this setup (the
    /// denominator); <paramref name="matched"/> are those confirmed. The confluence FSM owns those sets.
    /// </summary>
    public ConfluenceScore Score(IReadOnlySet<ConfluenceCondition> matched, IReadOnlySet<ConfluenceCondition> applicable)
    {
        ArgumentNullException.ThrowIfNull(matched);
        ArgumentNullException.ThrowIfNull(applicable);

        decimal applicableWeight = 0m;
        decimal matchedWeight = 0m;

        foreach (var condition in applicable)
        {
            if (!_options.Weights.TryGetValue(condition, out var weight))
            {
                continue;
            }

            applicableWeight += weight;
            if (matched.Contains(condition))
            {
                matchedWeight += weight;
            }
        }

        var score = applicableWeight <= 0m
            ? 0
            : (int)Math.Round(matchedWeight / applicableWeight * 100m, MidpointRounding.AwayFromZero);

        var allRequiredMatched = _options.RequiredConditions.All(matched.Contains);

        return new ConfluenceScore(score, GradeFor(score, allRequiredMatched), allRequiredMatched);
    }

    private SetupGrade GradeFor(int score, bool allRequiredMatched)
    {
        // A missing RequiredCondition is never tradeable (§2.5.2).
        if (!allRequiredMatched)
        {
            return SetupGrade.Reject;
        }

        // All RequiredConditions hold ⇒ this is the tradeable §2.5 model, so it grades at least B (TGR-4). The score
        // only promotes B → A at the A threshold; it can no longer demote a complete setup below B. The lower
        // (B/C) thresholds remain display-band labels for the raw score, not grading gates.
        return score >= _options.GradeAThreshold ? SetupGrade.A : SetupGrade.B;
    }
}
