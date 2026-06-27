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
    decimal? CommissionPerLotRoundTripUsd = null)
{
    public static InstrumentSettingsDto From(InstrumentOptionOverrides overrides) => new(
        overrides.MinRequiredConditions,
        overrides.RequiredConditions?.Select(c => c.ToString()).ToArray(),
        overrides.MinStopDistancePips,
        overrides.SpreadBasePips,
        overrides.CommissionPerLotRoundTripUsd);

    /// <summary>Maps to the domain override (parsing the condition names); throws <see cref="ArgumentException"/> on an
    /// unknown condition name.</summary>
    public InstrumentOptionOverrides ToOverrides() => new()
    {
        MinRequiredConditions = MinRequiredConditions,
        RequiredConditions = ParseConditions(RequiredConditions),
        MinStopDistancePips = MinStopDistancePips,
        SpreadBasePips = SpreadBasePips,
        CommissionPerLotRoundTripUsd = CommissionPerLotRoundTripUsd,
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

/// <summary>The current live settings snapshot (plan §15). Slice 1 carries the per-instrument overrides; the global
/// concept knobs + the economic calendar are added in the following slices.</summary>
public sealed record SettingsDto(IReadOnlyDictionary<string, InstrumentSettingsDto> InstrumentOverrides);
