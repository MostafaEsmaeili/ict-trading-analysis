using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Instruments;

/// <summary>
/// The built-in, extensible <see cref="IInstrumentRegistry"/> (plan §2.5.7 caveat 3). It maps the known FX majors
/// to the existing FX-major geometry (<see cref="InstrumentClass.Fx"/>, no overrides → byte-identical to the prior
/// hardcoded <c>SymbolSpec.FxMajor</c>) and <c>NAS100USD</c> to the NASDAQ-100 index profile
/// (<see cref="InstrumentClass.Index"/> → the §2.5.7 index killzone, point geometry, point-based costs, and the
/// TIME-10 macro reference). An unrecognised symbol falls back to the FX-default profile with
/// <see cref="InstrumentProfile.IsKnown"/> = false, so today's behaviour is preserved for symbols not yet listed
/// while a caller can log the fallback. Pure, deterministic, thread-safe (immutable after construction).
///
/// <para><b>Index override provenance (ICT is SILENT on every numeric index threshold).</b> The 2022 Mentorship
/// is a NASDAQ e-mini index mentorship teaching indices in HANDLES (1 handle = 1.00 point; Ep1 L214-216/L314-317),
/// so the point geometry and the macro reference are DERIVED, the OANDA CFD spec is CONVENTION, and every numeric
/// noise guard is INVENTED — each flagged on <see cref="InstrumentOptionOverrides"/> and surfaced in appsettings.</para>
/// </summary>
public sealed class InstrumentCatalog : IInstrumentRegistry
{
    /// <summary>The NASDAQ-100 index symbol this catalog recognises (OANDA's CFD ticker).</summary>
    public const string Nas100Symbol = "NAS100USD";

    // The built-in FX majors (normalised, upper-invariant). Anything not here OR NAS100USD falls back to FX-default
    // (IsKnown=false). This is the same FX-major set the system has always implicitly scanned as FxMajor.
    private static readonly IReadOnlySet<string> FxMajors = new HashSet<string>(StringComparer.Ordinal)
    {
        "EURUSD", "GBPUSD", "USDJPY", "AUDUSD", "USDCHF", "USDCAD", "NZDUSD", "EURGBP",
        "EURJPY", "GBPJPY", "XAUUSD",
    };

    // ---- NASDAQ-100 index option overrides (ICT-spec-reviewed; provenance flagged per field on the VO) ----

    /// <summary>Index stop-distance noise floor (points). INVENTED — ICT gives no NQ stop floor; echoes the FX 10 as points.</summary>
    private const decimal IndexMinStopDistancePoints = 10m;

    /// <summary>Index stop buffer beyond the swept extreme (points). RULE derived (§2.5.5 "1–2 ticks"); MAGNITUDE INVENTED.</summary>
    private const decimal IndexStopBufferPoints = 2m;

    /// <summary>Index consumed-liquidity exclusion band (points). INVENTED — no ICT number; FX 1.5 is too tight for NAS100.</summary>
    private const decimal IndexSweptLevelExclusionPoints = 2m;

    /// <summary>Index minimum FVG gap floor (points). DERIVED choice — 0 so the self-scaling ATR gate carries the quality floor.</summary>
    private const decimal IndexFvgMinGapPoints = 0m;

    /// <summary>Index stacked-FVG proximity band (points). INVENTED — no ICT number; FX 5 is too tight for NAS100 stacked gaps.</summary>
    private const decimal IndexFvgStackProximityPoints = 10m;

    /// <summary>Index relative-equal liquidity tolerance (points). INVENTED — no ICT number; FX 1.5 is too tight for NAS100.</summary>
    private const decimal IndexEqualLevelTolerancePoints = 2m;

    /// <summary>Index EG-3 close-proximity entry band half-width (points). INVENTED — already flagged; only live under the opt-in.</summary>
    private const decimal IndexCloseProximityTolerancePoints = 2m;

    /// <summary>Index round-trip dealing spread (points). CONVENTION — OANDA NAS100 CFD ~1.0 point during the active AM killzone.</summary>
    private const decimal IndexSpreadBasePoints = 1.0m;

    /// <summary>Index round-trip commission per unit. CONVENTION — OANDA NAS100 CFD is commission-free.</summary>
    private const decimal IndexCommissionPerUnitUsd = 0m;

    private static readonly InstrumentOptionOverrides IndexOverrides = new()
    {
        MinStopDistancePips = IndexMinStopDistancePoints,
        StopBufferPips = IndexStopBufferPoints,
        SweptLevelExclusionPips = IndexSweptLevelExclusionPoints,
        FvgMinGapPips = IndexFvgMinGapPoints,
        FvgStackProximityPips = IndexFvgStackProximityPoints,
        EqualLevelTolerancePips = IndexEqualLevelTolerancePoints,
        CloseProximityTolerancePips = IndexCloseProximityTolerancePoints,
        SpreadBasePips = IndexSpreadBasePoints,
        CommissionPerLotRoundTripUsd = IndexCommissionPerUnitUsd,

        // TIME-10 (CONTESTED-~80%) resolution: the index AM killzone opens at 08:30, so the Judas read consults the
        // 08:30 macro open alongside midnight (Ep17 L154-159). FX stays off (byte-identical). Set HERE, never branched
        // on InstrumentClass inside detector code (TIME-10's explicit-flag mandate). See docs/ict-core-model-decisions.md.
        UseMacroOpenReference = true,
    };

    /// <summary>The default singleton catalog (the built-in FX majors + NAS100USD). Construction is cheap; this is the
    /// convenient shared instance for code paths that have no DI-injected registry.</summary>
    public static InstrumentCatalog Default { get; } = new();

    public InstrumentProfile Resolve(Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        var key = symbol.Value; // Symbol normalises to trimmed upper-invariant on construction.

        if (key == Nas100Symbol)
        {
            return new InstrumentProfile(
                symbol,
                SymbolSpec.Nas100(symbol),
                ContractSpec.Nas100(symbol),
                IndexOverrides,
                isKnown: true);
        }

        // Known FX major → the existing FX-major geometry, NO overrides (byte-identical to the prior FxMajor path).
        // Unknown symbol → the SAME FX-major fallback, but flagged IsKnown=false so the caller can log it. The
        // fallback is deliberately today's behaviour, so an uncatalogued symbol scans exactly as it did before.
        var isKnown = FxMajors.Contains(key);
        return new InstrumentProfile(
            symbol,
            SymbolSpec.FxMajor(symbol),
            ContractSpec.FxMajor(symbol),
            InstrumentOptionOverrides.None,
            isKnown);
    }
}
