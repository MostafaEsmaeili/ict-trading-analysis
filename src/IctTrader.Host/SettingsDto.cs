using IctTrader.Domain.Detection;
using IctTrader.Domain.Instruments;

namespace IctTrader.Host;

/// <summary>
/// The editable per-instrument settings on the wire (plan §15 — live, UI-editable tuning). It carries the operator's
/// OVERRIDE for a symbol: the tuning (the k-of-n count + the required-condition subset, by member name) plus a few
/// per-instrument cost/geometry knobs. Fields left null fall back to the built-in catalog profile (the index geometry
/// the catalog resolves), so an override need only carry what it changes. Enum members are STRINGS (the API
/// convention), not numbers.
/// </summary>
public sealed record InstrumentSettingsDto(
    int? MinRequiredConditions = null,
    IReadOnlyList<string>? RequiredConditions = null,
    decimal? MinStopDistancePips = null,
    decimal? SpreadBasePips = null,
    decimal? CommissionPerLotRoundTripUsd = null,
    bool? RequireReferenceOpenAgreement = null)
{
    public static InstrumentSettingsDto From(InstrumentOptionOverrides overrides) => new(
        overrides.MinRequiredConditions,
        overrides.RequiredConditions?.Select(c => c.ToString()).ToArray(),
        overrides.MinStopDistancePips,
        overrides.SpreadBasePips,
        overrides.CommissionPerLotRoundTripUsd,
        overrides.RequireReferenceOpenAgreement);

    /// <summary>Maps to the domain override (parsing the condition names); throws <see cref="ArgumentException"/> on an
    /// unknown condition name.</summary>
    public InstrumentOptionOverrides ToOverrides() => new()
    {
        MinRequiredConditions = MinRequiredConditions,
        RequiredConditions = ParseConditions(RequiredConditions),
        MinStopDistancePips = MinStopDistancePips,
        SpreadBasePips = SpreadBasePips,
        CommissionPerLotRoundTripUsd = CommissionPerLotRoundTripUsd,
        RequireReferenceOpenAgreement = RequireReferenceOpenAgreement,
    };

    private static IReadOnlyList<ConfluenceCondition>? ParseConditions(IReadOnlyList<string>? names)
    {
        if (names is null)
        {
            return null;
        }

        var parsed = new List<ConfluenceCondition>(names.Count);
        foreach (var name in names)
        {
            if (!Enum.TryParse<ConfluenceCondition>(name, ignoreCase: true, out var condition))
            {
                throw new ArgumentException($"Unknown confluence condition '{name}'.");
            }

            parsed.Add(condition);
        }

        return parsed;
    }
}

/// <summary>
/// The global ICT concept settings the scanner is running under (plan §2.5.3/§2.5.4/§5.1/§5.4), projected
/// READ-ONLY for the Settings page so the operator can SEE the whole model in the UI. These are bound from the
/// <c>Ict:*</c> sections at startup; the live-editable surface is the per-instrument override (above), which
/// already covers per-pair k-of-n, the required subset, and cost geometry. Enum members are STRINGS.
/// </summary>
public sealed record GlobalConceptSettingsDto(
    // Confluence + grading (Ict:Confluence)
    IReadOnlyList<string> RequiredConditions,
    int? MinRequiredConditions,
    IReadOnlyDictionary<string, decimal> Weights,
    int GradeAThreshold,
    int GradeBThreshold,
    int GradeCThreshold,
    string AlertMinimumGrade,
    // Risk (Ict:Risk)
    decimal BaseRiskPercent,
    decimal MaxOpenPortfolioRiskPercent,
    decimal HardMaxRiskPercent,
    decimal MinStopDistancePips,
    IReadOnlyList<decimal> LossLadderPercents,
    int ConsecutiveWinsForLowestUnit,
    decimal DipRecoveryFraction,
    // Execution (Ict:Execution)
    decimal SpreadBasePips,
    decimal CommissionPerLotRoundTripUsd,
    // Scanning (Ict:Scanning)
    IReadOnlyList<string> ActiveKillzones,
    IReadOnlyList<string> ActiveStyles);

/// <summary>
/// The current live settings snapshot (plan §15): the editable per-instrument overrides, the read-only global
/// concept settings, and the set of confluence conditions a per-instrument required-subset may be drawn from
/// (the §2.5.2 canonical required set — what the subset checkboxes offer). The economic calendar is added in slice 3.
/// </summary>
public sealed record SettingsDto(
    IReadOnlyDictionary<string, InstrumentSettingsDto> InstrumentOverrides,
    GlobalConceptSettingsDto Global,
    IReadOnlyList<string> AvailableRequiredConditions,
    IReadOnlyList<string> AvailableInstruments);
