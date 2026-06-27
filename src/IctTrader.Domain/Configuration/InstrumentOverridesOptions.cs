using IctTrader.Domain.Instruments;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Operator-tunable PER-INSTRUMENT overrides (bound from <c>Ict:Instruments</c>) — the home for a pair's BAKED
/// tuning result, so a winning backtest setting becomes the LIVE default for that symbol without recompiling. Each
/// entry overlays its non-null fields ON TOP of the built-in per-class values the <see cref="InstrumentCatalog"/>
/// resolves (so config wins where set, the built-in index geometry survives where config is silent). The headline
/// knob is <see cref="InstrumentOptionOverrides.MinRequiredConditions"/> (the k-of-n relaxation, e.g. NAS100 → 6),
/// but any per-instrument scalar (spread, min-stop, …) can be tuned here too. Empty by default = the catalog's
/// built-in profiles, byte-identical.
/// <para>The dictionary key is the dashboard symbol (e.g. <c>NAS100USD</c>); lookup is case-insensitive. Example:
/// <c>"Ict": { "Instruments": { "Overrides": { "NAS100USD": { "MinRequiredConditions": 6 } } } }</c>.</para>
/// </summary>
public sealed class InstrumentOverridesOptions
{
    public const string SectionName = "Ict:Instruments";

    /// <summary>The §2.5.2 required-set size — the upper bound a per-instrument k-of-n may take (8 by default).</summary>
    private static readonly int RequiredSetSize = ConfluenceOptions.DefaultRequiredConditions.Count;

    /// <summary>Per-symbol overrides, keyed by dashboard symbol. A dictionary, so the config binder MERGES by key
    /// (no list-append hazard); empty default = no overrides.</summary>
    public Dictionary<string, InstrumentOptionOverrides> Overrides { get; init; } = [];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        foreach (var (symbol, o) in Overrides)
        {
            if (o.MinRequiredConditions is { } k && (k < 1 || k > RequiredSetSize))
            {
                errors.Add(
                    $"Ict:Instruments:Overrides:{symbol}:MinRequiredConditions must be within [1, {RequiredSetSize}] " +
                    $"but was {k}.");
            }
        }

        return errors;
    }
}
