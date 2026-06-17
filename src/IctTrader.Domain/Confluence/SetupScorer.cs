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
/// Pure domain service that turns matched confluences into a 0–100 score and an A/B/C/Reject grade
/// (plan §2.5.4): <c>score = Σ(matched weights)/Σ(applicable weights) ×100</c>; any missing
/// RequiredCondition ⇒ Reject regardless of score. All thresholds/weights come from
/// <see cref="ConfluenceOptions"/> — no magic numbers, fully operator-tunable.
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
        if (!allRequiredMatched)
        {
            return SetupGrade.Reject;
        }

        if (score >= _options.GradeAThreshold)
        {
            return SetupGrade.A;
        }

        if (score >= _options.GradeBThreshold)
        {
            return SetupGrade.B;
        }

        return score >= _options.GradeCThreshold ? SetupGrade.C : SetupGrade.Reject;
    }
}
