namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable fair-value-gap detection (plan §2.5.1 step 6, §2.5.7 caveat 5, §2.5.10). The displacement-quality
/// floors (<see cref="MinGapPips"/>, <see cref="AtrMultiple"/>) are flagged engineering placeholders needing
/// WP1 calibration; the two-touch void count and the correct-half gate encode the verified rules. Bound from
/// <c>Ict:Detection:Fvg</c>.
/// </summary>
public sealed class FvgOptions
{
    public const string SectionName = "Ict:Detection:Fvg";

    /// <summary>Minimum gap size in pips for a quality FVG (§2.5.7 caveat 5 placeholder).</summary>
    public decimal MinGapPips { get; init; } = 1.0m;

    public decimal AtrMultiple { get; init; } = 1.5m;

    public int AtrPeriod { get; init; } = 14;

    /// <summary>The retrace count that voids the gap — the 3rd tap voids (Ep38). FVG formation is touch 0; the first
    /// retrace into the void is touch 1 (FVG-SEM-1b).</summary>
    public int VoidOnTouchCount { get; init; } = 3;

    /// <summary>How a return into the gap counts toward the void (FVG-SEM-1a) — wick-into (Ep38 default) vs close-into.</summary>
    public FvgTouchSemantics TouchSemantics { get; init; } = FvgTouchSemantics.WickInto;

    public bool MitigateOnFullFill { get; init; } = true;

    /// <summary>Require the FVG to be in the correct premium/discount half to count as a confluence.</summary>
    public bool RequireInCorrectHalf { get; init; } = true;

    public decimal EquilibriumPercent { get; init; } = 0.50m;

    public decimal StackProximityPips { get; init; } = 5m;

    /// <summary>
    /// FVG-SEM-2a (Ep3 L376-394): select the entry FVG by Ep3's "first higher fair value gap" — the SHALLOWEST
    /// in-band gap on the displacement-leg timeframe (closest to the terminus) — instead of the nearest-sweet-spot
    /// array level. Default OFF keeps the existing selection byte-identical; pairs with <see cref="StackProximityPips"/>
    /// for stacked detection. No <c>Validate()</c> rule (a bool is always in contract).
    /// </summary>
    public bool StrictFirstFvg { get; init; }

    /// <summary>
    /// FVG-SEM-3 (§2.5.10): when on, VETO a freshly-formed FVG that falls in the Asian session window or has
    /// overlapping c1/c3 wicks — the two validity exclusions the confluence FSM does NOT already own (no-sweep,
    /// counter-bias, and no-CHoCH are already RequiredConditions, so they are annotated as evidence but never
    /// vetoed here). The five exclusion predicates are ALWAYS computed and attached as evidence regardless of
    /// this flag (so the dashboard has the diagnostic). Default OFF keeps the gap match byte-identical. No
    /// <c>Validate()</c> rule (a bool is always in contract).
    /// </summary>
    public bool ApplyValidityExclusions { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (VoidOnTouchCount < 1)
        {
            errors.Add($"VoidOnTouchCount must be at least 1 but was {VoidOnTouchCount}.");
        }

        if (MinGapPips < 0m)
        {
            errors.Add($"MinGapPips cannot be negative but was {MinGapPips}.");
        }

        if (AtrPeriod < 1)
        {
            errors.Add($"AtrPeriod must be at least 1 but was {AtrPeriod}.");
        }

        if (AtrMultiple < 0m)
        {
            errors.Add($"AtrMultiple cannot be negative but was {AtrMultiple}.");
        }

        if (EquilibriumPercent is < 0m or > 1m)
        {
            errors.Add($"EquilibriumPercent must be within [0, 1] but was {EquilibriumPercent}.");
        }

        if (StackProximityPips < 0m)
        {
            errors.Add($"StackProximityPips cannot be negative but was {StackProximityPips}.");
        }

        if (!Enum.IsDefined(TouchSemantics))
        {
            errors.Add($"TouchSemantics must be a valid {nameof(FvgTouchSemantics)} value but was {(int)TouchSemantics}.");
        }

        return errors;
    }
}
