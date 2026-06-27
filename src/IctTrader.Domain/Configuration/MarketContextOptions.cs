using IctTrader.Domain.Sessions;
using IctTrader.Domain.Styles;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable per-symbol scanning state (plan §4.1/§4.6). The ring-buffer depth, the per-type open-array cap,
/// the midnight session reset, and the operator-selected active killzones/styles are all bound from
/// <c>Ict:Scanning</c> — nothing here is a literal. The Host validates this via <see cref="Validate"/>.
/// </summary>
public sealed class MarketContextOptions
{
    public const string SectionName = "Ict:Scanning";

    public int WindowCapacity { get; init; } = 512;

    public int MaxOpenArraysPerType { get; init; } = 64;

    public bool ResetSessionStateAtNyMidnight { get; init; } = true;

    /// <summary>
    /// Whether the Judas reference open consults the 08:30 NY macro open alongside the midnight open
    /// (TIME-10 / Ep17 L154-159 — an FX New-York-session rule: when bearish use the LOWER of the two opens,
    /// when bullish the higher). Default <c>false</c> keeps the FX behaviour midnight-only and byte-identical;
    /// it is an EXPLICIT operator flag, never auto-derived from <see cref="InstrumentClass"/> (the index
    /// auto-switch is deliberately deferred). The macro open is still CAPTURED when off (dashboard-useful);
    /// only the reference resolution ignores it.
    /// </summary>
    public bool UseMacroOpenReference { get; init; }

    /// <summary>
    /// The New-York wall-clock time of the macro reference open (TIME-10). Defaults to the 08:30 NY macro
    /// (Ep4/Ep5/Ep7/Ep10), captured on the first candle of the day whose NY open time is at/after this.
    /// </summary>
    public TimeOnly MacroReferenceOpenTime { get; init; } = new(8, 30);

    // The operator-selected lists default to EMPTY, not the business default. This is load-bearing: the .NET
    // configuration binder APPENDS bound array items to a pre-populated collection initializer rather than
    // replacing it, so a non-empty default would be silently prepended to the operator's config — e.g.
    // `Ict:Scanning:ActiveStyles=["Intraday"]` bound onto a `[Intraday]` default yields `[Intraday, Intraday]`,
    // and the candle handler would then feed every candle to the same scanner TWICE (corrupting its state so no
    // setup ever confirms), while `["Scalp"]` would silently still run Intraday too. With an empty default the
    // binder replaces cleanly; the ICT business default is applied by the Resolved* accessors below (which the
    // scanner consumes), and an empty (unconfigured) list falls back to that default there.
    public IReadOnlyList<Killzone> ActiveKillzones { get; init; } = [];

    public IReadOnlyList<TradeStyle> ActiveStyles { get; init; } = [];

    private static readonly IReadOnlyList<Killzone> DefaultActiveKillzones =
        [Killzone.LondonOpen, Killzone.NewYorkOpen];

    private static readonly IReadOnlyList<TradeStyle> DefaultActiveStyles = [TradeStyle.Intraday];

    /// <summary>
    /// The active killzones the scanner hunts — the configured set de-duplicated, or the ICT default
    /// (London Open + New York) when none is configured. Consume this, never the raw <see cref="ActiveKillzones"/>.
    /// </summary>
    public IReadOnlyList<Killzone> ResolvedActiveKillzones =>
        ActiveKillzones.Count == 0 ? DefaultActiveKillzones : ActiveKillzones.Distinct().ToArray();

    /// <summary>
    /// The active trade styles the scanner runs — the configured set de-duplicated, or the ICT default
    /// (Intraday — the §2.5 model) when none is configured. Consume this, never the raw <see cref="ActiveStyles"/>:
    /// a duplicate style would feed each candle to the same per-(symbol, style) scanner more than once.
    /// </summary>
    public IReadOnlyList<TradeStyle> ResolvedActiveStyles =>
        ActiveStyles.Count == 0 ? DefaultActiveStyles : ActiveStyles.Distinct().ToArray();

    /// <summary>
    /// The killzones an operator may enable via <c>Ict:Scanning:ActiveKillzones</c> — the FROZEN CONTRACT
    /// subset (plan §11.1). <see cref="Killzone.Pm"/>/<see cref="Killzone.Am"/> are internal classification
    /// outcomes (FX afternoon / index morning) governed by instrument class, not operator-selectable here;
    /// <see cref="Killzone.None"/> is not a killzone.
    /// <para>FVG-SEM-3 (Ep10): <see cref="Killzone.Asian"/> is the LOW-PRIORITY opt-in — selectable here, but
    /// deliberately excluded from the default <see cref="ActiveKillzones"/> ("deprioritized" = off by default,
    /// NOT a lower confluence weight, which would change Σ(applicable) and break grading). Enable it explicitly
    /// to hunt the Asian window.</para>
    /// </summary>
    public static IReadOnlyList<Killzone> SelectableKillzones { get; } =
        [Killzone.Asian, Killzone.LondonOpen, Killzone.NewYorkOpen, Killzone.LondonClose];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (WindowCapacity < 3)
        {
            errors.Add($"WindowCapacity must be at least 3 (for 3-candle patterns) but was {WindowCapacity}.");
        }

        if (MaxOpenArraysPerType < 1)
        {
            errors.Add($"MaxOpenArraysPerType must be at least 1 but was {MaxOpenArraysPerType}.");
        }

        // An empty configured list is VALID — it means "use the ICT default" (applied by the Resolved* accessors).
        // We validate the CONFIGURED members (a typo'd killzone must still fail fast); the resolved lists are
        // guaranteed non-empty by construction so there is no "at least one" check.
        foreach (var killzone in ActiveKillzones.Where(k => !SelectableKillzones.Contains(k)))
        {
            errors.Add($"ActiveKillzones must be a subset of [{string.Join(", ", SelectableKillzones)}] but contained {killzone}.");
        }

        // The macro reference must be a sane pre-lunch morning time (TIME-10): 00:00 would collide with the
        // midnight open, and a >= noon value is past the macro window (and into the hard lunch block).
        if (MacroReferenceOpenTime <= TimeOnly.MinValue || MacroReferenceOpenTime >= new TimeOnly(12, 0))
        {
            errors.Add(
                $"MacroReferenceOpenTime must be after 00:00 and before 12:00 but was {MacroReferenceOpenTime:HH\\:mm}.");
        }

        return errors;
    }
}
