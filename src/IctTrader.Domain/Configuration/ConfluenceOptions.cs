using IctTrader.Domain.Detection;
using IctTrader.Domain.Setups;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable confluence scoring + grading (plan §2.5.3/§2.5.4). EVERYTHING is configurable: the per-condition
/// weights, which conditions are required, the A/B/C grade thresholds, and the alert floor — all bound from
/// <c>Ict:Confluence</c> with the transcript-verified values as defaults. The Host validates this via
/// <c>ValidateOnStart</c> by calling <see cref="Validate"/>.
/// </summary>
public sealed class ConfluenceOptions
{
    public const string SectionName = "Ict:Confluence";

    /// <summary>Per-condition weight 0..1 (§2.5.3). Conditions absent from the map are unweighted (e.g. hard gates).</summary>
    public IReadOnlyDictionary<ConfluenceCondition, decimal> Weights { get; init; } = DefaultWeights;

    /// <summary>Conditions that MUST be matched for an A/B grade; any missing required ⇒ Reject (§2.5.2/§2.5.4).</summary>
    public IReadOnlyList<ConfluenceCondition> RequiredConditions { get; init; } = DefaultRequiredConditions;

    /// <summary>The score at or above which an all-required setup is promoted from B to A (§2.5.4 — the one
    /// score-driven grading gate; an all-required setup below it is still a tradeable B, TGR-4).</summary>
    public int GradeAThreshold { get; init; } = 80;

    /// <summary>A display-band label for the raw score (the §2.5.4 "65 B-floor"); since TGR-4 it no longer gates the
    /// grade (all-required ⇒ at least B). Retained for the dashboard score bands + config back-compat.</summary>
    public int GradeBThreshold { get; init; } = 65;

    /// <summary>A display-band label for the raw score (the §2.5.4 "50 C-floor"); since TGR-4 it no longer gates the
    /// grade. Retained for the dashboard score bands + config back-compat.</summary>
    public int GradeCThreshold { get; init; } = 50;

    /// <summary>Only setups graded at or above this fire an alert (default B ⇒ floor 65).</summary>
    public SetupGrade AlertMinimumGrade { get; init; } = SetupGrade.B;

    /// <summary>The §2.5.3 weights, verbatim and adversarially verified — the configurable defaults.</summary>
    public static IReadOnlyDictionary<ConfluenceCondition, decimal> DefaultWeights { get; } =
        new Dictionary<ConfluenceCondition, decimal>
        {
            [ConfluenceCondition.KillzoneEntry] = 1.00m,
            [ConfluenceCondition.LiquiditySweep] = 0.95m,
            [ConfluenceCondition.DisplacementMss] = 0.95m,
            [ConfluenceCondition.FvgPresent] = 0.90m,
            [ConfluenceCondition.BiasAligned] = 0.85m,
            [ConfluenceCondition.PremiumDiscountHalf] = 0.85m,
            [ConfluenceCondition.OteZone] = 0.70m,
            [ConfluenceCondition.OrderBlockConfluence] = 0.65m,
            [ConfluenceCondition.DrawTargetRrMet] = 0.65m,
            [ConfluenceCondition.SmtDivergence] = 0.55m,
            [ConfluenceCondition.OpenPriceReference] = 0.50m,
            [ConfluenceCondition.MacroTime] = 0.45m,
            [ConfluenceCondition.CleanPriceAction] = 0.40m,
            [ConfluenceCondition.CalendarDriver] = 0.35m,
        };

    /// <summary>The §2.5.2 RequiredConditions (CalendarClear is the hard calendar gate; bias folds in HTF-direction).</summary>
    public static IReadOnlyList<ConfluenceCondition> DefaultRequiredConditions { get; } =
    [
        ConfluenceCondition.BiasAligned,
        ConfluenceCondition.KillzoneEntry,
        ConfluenceCondition.LiquiditySweep,
        ConfluenceCondition.DisplacementMss,
        ConfluenceCondition.FvgPresent,
        ConfluenceCondition.PremiumDiscountHalf,
        ConfluenceCondition.DrawTargetRrMet,
        ConfluenceCondition.CalendarClear,
    ];

    /// <summary>Returns the configuration errors (empty when valid). The Host fails startup if any exist.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!(GradeAThreshold > GradeBThreshold && GradeBThreshold > GradeCThreshold && GradeCThreshold > 0))
        {
            errors.Add($"Grade thresholds must be descending and positive (A={GradeAThreshold}, B={GradeBThreshold}, C={GradeCThreshold}).");
        }

        if (GradeAThreshold > 100)
        {
            errors.Add($"GradeAThreshold must be <= 100 but was {GradeAThreshold}.");
        }

        foreach (var (condition, weight) in Weights)
        {
            if (weight is < 0m or > 1m)
            {
                errors.Add($"Weight for {condition} must be within [0, 1] but was {weight}.");
            }
        }

        if (RequiredConditions.Count == 0)
        {
            errors.Add("At least one RequiredCondition must be configured.");
        }

        if (!Enum.IsDefined(AlertMinimumGrade))
        {
            errors.Add($"AlertMinimumGrade must be a valid {nameof(SetupGrade)} value but was {(int)AlertMinimumGrade}.");
        }

        return errors;
    }
}
