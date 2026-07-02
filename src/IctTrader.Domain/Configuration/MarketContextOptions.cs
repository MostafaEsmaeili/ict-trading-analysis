using IctTrader.Domain.Instruments;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable per-symbol scanning state (plan §4.1/§4.6). The ring-buffer depth, the per-type open-array cap,
/// the midnight session reset, and the operator-selected active styles are all bound from
/// <c>Ict:Scanning</c> — nothing here is a literal. The killzone hunt-set (<c>Ict:Scanning:ActiveKillzones</c>)
/// is owned by <see cref="KillzoneEntryOptions"/> (the single source the gate reads). The Host validates this
/// via <see cref="Validate"/>.
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

    // The operator-selected list defaults to EMPTY, not the business default. This is load-bearing: the .NET
    // configuration binder APPENDS bound array items to a pre-populated collection initializer rather than
    // replacing it, so a non-empty default would be silently prepended to the operator's config — e.g.
    // `Ict:Scanning:ActiveStyles=["Intraday"]` bound onto a `[Intraday]` default yields `[Intraday, Intraday]`,
    // and the candle handler would then feed every candle to the same scanner TWICE (corrupting its state so no
    // setup ever confirms), while `["Scalp"]` would silently still run Intraday too. With an empty default the
    // binder replaces cleanly; the ICT business default is applied by the Resolved* accessor below (which the
    // scanner consumes), and an empty (unconfigured) list falls back to that default there.
    //
    // The killzone hunt-set (`Ict:Scanning:ActiveKillzones`) is owned by KillzoneEntryOptions — the SINGLE source
    // the detector + entry rung actually read — so it deliberately lives there, not here, even though both POCOs
    // bind the same `Ict:Scanning` section.
    public IReadOnlyList<TradeStyle> ActiveStyles { get; init; } = [];

    private static readonly IReadOnlyList<TradeStyle> DefaultActiveStyles = [TradeStyle.Intraday];

    /// <summary>
    /// The active trade styles the scanner runs — the configured set de-duplicated, or the ICT default
    /// (Intraday — the §2.5 model) when none is configured. Consume this, never the raw <see cref="ActiveStyles"/>:
    /// a duplicate style would feed each candle to the same per-(symbol, style) scanner more than once.
    /// </summary>
    public IReadOnlyList<TradeStyle> ResolvedActiveStyles =>
        ActiveStyles.Count == 0 ? DefaultActiveStyles : ActiveStyles.Distinct().ToArray();

    /// <summary>
    /// The operator-selected setup models the scanner runs (<c>Ict:Scanning:ActiveModels</c>, plan §16) —
    /// EMPTY default for the same config-binder reason as <see cref="ActiveStyles"/> (a non-empty initializer
    /// would be silently prepended to the operator's config). Consume <see cref="ResolvedActiveModels"/>.
    /// </summary>
    public IReadOnlyList<SetupModel> ActiveModels { get; init; } = [];

    private static readonly IReadOnlyList<SetupModel> DefaultActiveModels = [SetupModel.Ict2022];

    /// <summary>
    /// The active setup models — the configured set de-duplicated, or the business default when none is
    /// configured. Consume this, never the raw <see cref="ActiveModels"/>: a duplicate model would feed each
    /// candle to the same per-(symbol, timeframe, style, model) scanner more than once and corrupt its FSM.
    /// (The default flips to both models once the ICT 2024 pipeline registers — the operator's chosen live
    /// default; until then only the canonical §2.5 model scans.)
    /// </summary>
    public IReadOnlyList<SetupModel> ResolvedActiveModels =>
        ActiveModels.Count == 0 ? DefaultActiveModels : ActiveModels.Distinct().ToArray();

    /// <summary>
    /// The killzones an operator may enable via <c>Ict:Scanning:ActiveKillzones</c> — the FROZEN CONTRACT
    /// subset (plan §11.1). <see cref="Killzone.Pm"/>/<see cref="Killzone.Am"/> are internal classification
    /// outcomes (FX afternoon / index morning) governed by instrument class, not operator-selectable here;
    /// <see cref="Killzone.None"/> is not a killzone.
    /// <para>FVG-SEM-3 (Ep10): <see cref="Killzone.Asian"/> is the LOW-PRIORITY opt-in — selectable here, but
    /// deliberately excluded from the default hunt-set ("deprioritized" = off by default,
    /// NOT a lower confluence weight, which would change Σ(applicable) and break grading). Enable it explicitly
    /// to hunt the Asian window.</para>
    /// </summary>
    public static IReadOnlyList<Killzone> SelectableKillzones { get; } =
        [Killzone.Asian, Killzone.LondonOpen, Killzone.NewYorkOpen, Killzone.LondonClose];

    /// <summary>
    /// Returns a copy with the instrument-class scalar overrides applied where present
    /// (<see cref="UseMacroOpenReference"/>). A <see cref="InstrumentOptionOverrides.None"/> / FX bundle leaves the
    /// flag off (byte-identical, midnight-only Judas reference); the index sets it ON — the TIME-10
    /// CONTESTED-~80% resolution — so the 08:30 macro open anchors the Judas read alongside midnight. This is the
    /// explicit-flag seam TIME-10 mandated: the flag is set HERE by the catalog, never branched on
    /// <see cref="InstrumentClass"/> inside detector code. The macro CAPTURE time, window sizing, midnight reset,
    /// and active styles are instrument-agnostic and unchanged.
    /// </summary>
    public MarketContextOptions WithInstrumentOverrides(InstrumentOptionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return new MarketContextOptions
        {
            WindowCapacity = WindowCapacity,
            MaxOpenArraysPerType = MaxOpenArraysPerType,
            ResetSessionStateAtNyMidnight = ResetSessionStateAtNyMidnight,
            UseMacroOpenReference = overrides.UseMacroOpenReference ?? UseMacroOpenReference,
            MacroReferenceOpenTime = MacroReferenceOpenTime,
            ActiveStyles = ActiveStyles,
            ActiveModels = ActiveModels,
        };
    }

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
