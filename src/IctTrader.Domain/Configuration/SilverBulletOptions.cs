using IctTrader.Domain.Sessions;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// The ICT "Silver Bullet" macro overlay (bound from <c>Ict:Scanning:SilverBullet</c>) — an OPT-IN, NON-classifying
/// time-of-day narrowing of the §2.5.2 <see cref="IctTrader.Domain.Detection.ConfluenceCondition.KillzoneEntry"/>
/// RequiredCondition. When <see cref="Enabled"/>, a setup may confirm only if the candle is BOTH inside an active
/// killzone (unchanged) AND inside one of the enabled Silver-Bullet macro windows — so the operator can hunt the
/// Silver Bullet specifically (canonically best on the NQ/ES indices). It is an INTERSECTION narrowing of an existing
/// gate: it adds NO <see cref="IctTrader.Domain.Detection.ConfluenceCondition"/> weight (Σ=9.75 untouched), never widens
/// the hunt-set (AND, not OR), and composes with the hard lunch + index last-entry rules (it can never re-open them).
/// <para>
/// <b>PROVENANCE — flagged.</b> The named "Silver Bullet" does NOT appear in the 2022 Mentorship transcripts (only the
/// idiom). It is Primer/community/later-ICT canon — so EVERY window here is provenance-flagged: NOT Mentorship-verbatim.
/// The 2022 model (Ep17) actually stops new FX entries at 10:00 and treats 10:00–11:00 NY as the LondonClose window
/// (FX) / the back half of IndexAm 08:30–11:00 (index). The default macro is the single 10:00–11:00 window (the canonical
/// AM Silver Bullet); the community 03:00–04:00 and 14:00–15:00 macros are opt-in by configuring <see cref="MacroWindows"/>.
/// </para>
/// <para>
/// <b>Composition.</b> For an INDEX symbol the overlay narrows IndexAm (08:30–11:00) to the macro (e.g. 10:00–10:40 after
/// the existing 10:40 last-entry cutoff) — it does NOT add a window. For FX the 10:00–11:00 macro requires
/// <c>LondonClose</c> in the active hunt-set (10–11 classifies as LondonClose for FX); the 03–04 / 14–15 macros require
/// their own parent killzones to be active too, since the overlay only NARROWS an already-active killzone.
/// </para>
/// </summary>
public sealed class SilverBulletOptions
{
    public const string SectionName = "Ict:Scanning:SilverBullet";

    /// <summary>When false (the config default) the overlay is a no-op — the <c>KillzoneEntry</c> gate is byte-identical
    /// to today. When true it AND-requires the candle to fall inside an enabled macro window.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The Silver-Bullet macro windows (NY local, inclusive-start/exclusive-end via <see cref="SessionWindow"/>).
    /// Defaults to EMPTY so the .NET config binder REPLACES rather than APPENDS to a pre-populated initializer (see
    /// <see cref="MarketContextOptions"/> for the documented rationale) — consume <see cref="ResolvedMacroWindows"/>,
    /// never this. All windows are PROVENANCE-FLAGGED Primer/community, NOT Mentorship-verbatim.
    /// </summary>
    public IReadOnlyList<SessionWindow> MacroWindows { get; init; } = [];

    /// <summary>The canonical AM Silver Bullet (10:00–11:00 NY) — the default when none is configured.</summary>
    private static readonly IReadOnlyList<SessionWindow> DefaultMacroWindows =
        [new SessionWindow(new TimeOnly(10, 0), new TimeOnly(11, 0))];

    /// <summary>The macro windows to consume — the configured set, or the canonical 10:00–11:00 default when none is set.</summary>
    public IReadOnlyList<SessionWindow> ResolvedMacroWindows =>
        MacroWindows.Count == 0 ? DefaultMacroWindows : MacroWindows;

    /// <summary>True if <paramref name="newYorkTimeOfDay"/> falls inside any enabled macro window.</summary>
    public bool ContainsMacro(TimeOnly newYorkTimeOfDay) =>
        ResolvedMacroWindows.Any(w => w.Contains(newYorkTimeOfDay));

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        // A null configured list is the only structural error — an empty list is VALID (means "use the default", applied
        // by the resolved accessor), and each SessionWindow already self-validates start != end in its constructor.
        if (MacroWindows is null)
        {
            errors.Add("MacroWindows must not be null (use an empty list to mean the default 10:00–11:00 macro).");
        }

        return errors;
    }
}
