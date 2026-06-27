using IctTrader.Domain.Detection;
using IctTrader.Domain.Instruments;
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

    /// <summary>
    /// Conditions that MUST be matched for an A/B grade; any missing required ⇒ Reject (§2.5.2/§2.5.4). Defaults to
    /// EMPTY so the .NET config binder REPLACES rather than APPENDS to a pre-populated initializer (see
    /// MarketContextOptions.cs for the documented rationale) — a non-empty default would be prepended to the
    /// operator's set, so a relaxation (removing a required condition) would be silently ignored. Consume
    /// <see cref="EffectiveRequiredConditions"/>, never this. (<see cref="Weights"/> is a dictionary, merged by key,
    /// so it is not affected.)
    /// </summary>
    public IReadOnlyList<ConfluenceCondition> RequiredConditions { get; init; } = [];

    /// <summary>The required set to enforce — the configured set, or the §2.5.2 defaults when none is configured.</summary>
    public IReadOnlyList<ConfluenceCondition> EffectiveRequiredConditions =>
        RequiredConditions.Count == 0 ? DefaultRequiredConditions : RequiredConditions;

    /// <summary>
    /// The MINIMUM number of <see cref="EffectiveRequiredConditions"/> that must match to confirm — the "k of n"
    /// relaxation of the §2.5.2 all-AND gate. <c>null</c> (the default) = ALL required, i.e. the strict, canonical
    /// §2.5 model (byte-identical to before this knob existed). When set to <c>k &lt; n</c> it is an EXPERIMENTAL,
    /// explicitly NON-canonical relaxation: a setup may confirm with only k of the required conditions, but ONLY if
    /// its weighted §2.5.4 score still clears the <see cref="AlertMinimumGrade"/> floor — so grading is handed back
    /// to the score (the all-AND auto-B of TGR-4 applies only to a fully-complete setup). Use it to discover, via the
    /// backtest optimizer, whether a relaxed combination outperforms the strict model on a given asset/timeframe;
    /// keep it null in production unless a backtest justifies otherwise.
    /// </summary>
    public int? MinRequiredConditions { get; init; }

    /// <summary>Returns a copy with <see cref="MinRequiredConditions"/> overridden — the per-run knob the backtest
    /// engine + optimizer vary without mutating the host's shared options.</summary>
    public ConfluenceOptions WithMinRequiredConditions(int? minRequiredConditions) => new()
    {
        Weights = Weights,
        RequiredConditions = RequiredConditions,
        GradeAThreshold = GradeAThreshold,
        GradeBThreshold = GradeBThreshold,
        GradeCThreshold = GradeCThreshold,
        AlertMinimumGrade = AlertMinimumGrade,
        MinRequiredConditions = minRequiredConditions,
    };

    /// <summary>
    /// Applies a symbol's per-instrument confluence override (its baked tuning result, e.g. NAS100 → 6-of-8). The
    /// explicit per-run <see cref="MinRequiredConditions"/> (set by a backtest request / optimizer combo) WINS over
    /// the instrument's baked value, so a sweep can still override it; an unset per-run value falls back to the
    /// instrument's <see cref="InstrumentOptionOverrides.MinRequiredConditions"/>. An FX
    /// <see cref="InstrumentOptionOverrides.None"/> bundle leaves this unchanged (strict, byte-identical). The
    /// scanner applies this LAST (after any per-run override), so the precedence holds.
    /// </summary>
    public ConfluenceOptions WithInstrumentOverrides(InstrumentOptionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return WithMinRequiredConditions(MinRequiredConditions ?? overrides.MinRequiredConditions);
    }

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

        // An empty CONFIGURED list is VALID — it means "use the §2.5.2 defaults" (applied by EffectiveRequiredConditions,
        // which is non-empty by construction). So there is no "at least one" check on the raw property.

        // The k-of-n relaxation, when set, must be a sane subset count: at least 1 (the model still needs structure)
        // and at most the full required set (≥ that IS the strict default — express it as null instead).
        if (MinRequiredConditions is { } k && (k < 1 || k > EffectiveRequiredConditions.Count))
        {
            errors.Add(
                $"MinRequiredConditions must be within [1, {EffectiveRequiredConditions.Count}] (the required-set size) " +
                $"but was {k}. Leave it null for the strict all-required §2.5 model.");
        }

        if (!Enum.IsDefined(AlertMinimumGrade))
        {
            errors.Add($"AlertMinimumGrade must be a valid {nameof(SetupGrade)} value but was {(int)AlertMinimumGrade}.");
        }

        return errors;
    }
}
