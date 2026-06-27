namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable gate for the OPTIONAL <c>MacroTime</c> confluence (plan §2.5.5/§2.5.8). The macro anchor TIMES (08:30 /
/// 09:30 / 13:30 / 15:00 NY — the algorithmic macro runs) are CONFORMANT; the window WIDTH
/// (<see cref="MacroWindowMinutes"/>) is INVENTED-flagged (the transcripts name the macros but not a precise
/// tolerance), so it is small + operator-tunable. A scoring-only confluence (default ON), never a hard gate. Bound
/// from <c>Ict:Detection:MacroTime</c>.
/// </summary>
public sealed class MacroTimeOptions
{
    public const string SectionName = "Ict:Detection:MacroTime";

    /// <summary>Whether the macro-time confluence is scored. Default ON — additive scoring only.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The half-width (in minutes) of the window centred on each macro anchor. INVENTED (provenance-flagged): the
    /// transcripts name the macros but not a precise tolerance, so it is small by default and operator-tunable.
    /// Inclusive at the edge (±this from an anchor).
    /// </summary>
    public int MacroWindowMinutes { get; init; } = 10;

    // The anchor list defaults to EMPTY so the .NET config binder REPLACES rather than APPENDS to a pre-populated
    // initializer (see MarketContextOptions.cs for the documented rationale); the §2.5.5 anchors are applied by the
    // ResolvedMacroAnchors accessor the detector consumes.
    public IReadOnlyList<TimeOnly> MacroAnchors { get; init; } = [];

    private static readonly IReadOnlyList<TimeOnly> DefaultMacroAnchors =
        [new(8, 30), new(9, 30), new(13, 30), new(15, 0)];

    /// <summary>The macro anchors to test — the configured set, or the §2.5.5 defaults when none is configured.
    /// Consume this, never the raw <see cref="MacroAnchors"/>.</summary>
    public IReadOnlyList<TimeOnly> ResolvedMacroAnchors =>
        MacroAnchors.Count == 0 ? DefaultMacroAnchors : MacroAnchors;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MacroWindowMinutes < 1)
        {
            errors.Add($"MacroWindowMinutes must be at least 1 but was {MacroWindowMinutes}.");
        }

        // An empty CONFIGURED list is VALID (it means "use the §2.5.5 defaults", applied by ResolvedMacroAnchors); a
        // NULL list is not.
        if (MacroAnchors is null)
        {
            errors.Add("MacroAnchors must not be null.");
        }

        return errors;
    }
}
