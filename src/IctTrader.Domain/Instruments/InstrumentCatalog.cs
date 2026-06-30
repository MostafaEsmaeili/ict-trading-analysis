using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Instruments;

/// <summary>
/// The built-in, extensible <see cref="IInstrumentRegistry"/> (plan §2.5.7 caveat 3). It maps the known FX majors
/// to the existing FX-major geometry (<see cref="InstrumentClass.Fx"/>, no overrides → byte-identical to the prior
/// hardcoded <c>SymbolSpec.FxMajor</c>), the US index CFDs (<c>NAS100USD</c>/<c>SPX500USD</c>/<c>US30USD</c>) to the
/// index profile (<see cref="InstrumentClass.Index"/> → the §2.5.7 index killzone, point geometry, point-based costs,
/// and the TIME-10 macro reference), and spot gold (<c>XAUUSD</c>) to the METAL profile (its own pip = 0.1 geometry,
/// but still <see cref="InstrumentClass.Fx"/> so it hunts the FX London/NY sessions ICT trades gold on).
/// An unrecognised symbol falls back to the FX-default profile with
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

    /// <summary>The S&amp;P 500 (E-mini ES proxy) index symbol — OANDA's CFD ticker. The 2022 Mentorship's co-primary
    /// index alongside the NASDAQ; same CFD point geometry + the §2.5.7 index killzone.</summary>
    public const string Spx500Symbol = "SPX500USD";

    /// <summary>The Dow Jones Industrial Average (US30 / Dow) index symbol — OANDA's CFD ticker. ICT names US30 as
    /// the THIRD core index vehicle alongside NQ/ES (the 2022 Mentorship is a NASDAQ e-mini mentorship); it reuses
    /// the identical OANDA CFD point geometry + the §2.5.7 index killzone + the 08:30 macro reference. NOTE: Dow's
    /// larger nominal level (tens of thousands of points) may warrant wider noise guards (stop buffer / exclusion /
    /// stacked-gap bands) than NAS100 — tunable later via the per-instrument override seam (<c>Ict:Instruments</c>);
    /// reusing <see cref="IndexOverrides"/> is the faithful, ICT-silent first cut.</summary>
    public const string Us30Symbol = "US30USD";

    /// <summary>Spot gold (XAU/USD) — OANDA's <c>XAU_USD</c> CFD (normalised to <c>XAUUSD</c>). The sole catalogued
    /// METAL: its own pip = 0.1 / tick = 0.01 geometry, but <see cref="InstrumentClass.Fx"/> so it hunts the FX
    /// London/NY killzones. Was previously (wrongly) in <see cref="FxMajors"/>, which mis-sized it.</summary>
    public const string XauusdSymbol = "XAUUSD";

    // The built-in US index CFDs (normalised, upper-invariant) — Michael Huddleston's primary vehicles (NQ + ES + Dow).
    // All resolve to InstrumentClass.Index (point geometry + the §2.5.7 AM killzone + the 08:30 macro reference).
    private static readonly IReadOnlySet<string> IndexSymbols = new HashSet<string>(StringComparer.Ordinal)
    {
        Nas100Symbol, Spx500Symbol, Us30Symbol,
    };

    // The built-in spot metals (normalised, upper-invariant). A metal resolves to its own pip-0.1 geometry but KEEPS
    // InstrumentClass.Fx (it hunts the FX London/NY sessions ICT trades gold on), so no new InstrumentClass value is
    // needed. Gold was moved OUT of FxMajors below — FX-major geometry (pip 0.0001) mis-sized a realistic $5 gold stop.
    private static readonly IReadOnlySet<string> Metals = new HashSet<string>(StringComparer.Ordinal)
    {
        XauusdSymbol,
    };

    // The built-in FX majors (normalised, upper-invariant). Anything not here OR an index/metal symbol falls back to
    // FX-default (IsKnown=false). This is the same FX-major set the system has always implicitly scanned as FxMajor,
    // MINUS XAUUSD (gold is now a Metals entry — see Metals/MetalOverrides; it never had FX-major geometry honestly).
    private static readonly IReadOnlySet<string> FxMajors = new HashSet<string>(StringComparer.Ordinal)
    {
        "EURUSD", "GBPUSD", "USDJPY", "AUDUSD", "USDCHF", "USDCAD", "NZDUSD", "EURGBP",
        "EURJPY", "GBPJPY",
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

    // ---- Spot-gold (XAU/USD) option overrides (ICT is SILENT on gold — EVERY value here is INVENTED/CONVENTION) ----
    // Pip semantics: a gold "pip" = 0.1 (SymbolSpec.Metal.PipSize = 0.1), so these *Pips values read as 0.1-units.
    // PROVENANCE: gold does NOT appear in the 2022 NASDAQ Mentorship (a SECONDARY/event-driven vehicle), so none of
    // these is Mentorship-verbatim — each is an operator-tunable noise guard scaled to gold's $-denominated 0.1 pip.

    /// <summary>Gold stop-distance noise floor (pips, pip = 0.1). INVENTED — ICT gives no gold stop floor; 30 pips ≈ $3.</summary>
    private const decimal MetalMinStopDistancePips = 30m;

    /// <summary>Gold stop buffer beyond the swept extreme (pips). RULE derived (§2.5.5 "1–2 ticks"); MAGNITUDE INVENTED — 5 pips ≈ $0.50.</summary>
    private const decimal MetalStopBufferPips = 5m;

    /// <summary>Gold consumed-liquidity exclusion band (pips). INVENTED — no ICT number; 5 pips ≈ $0.50 around equal levels.</summary>
    private const decimal MetalSweptLevelExclusionPips = 5m;

    /// <summary>Gold minimum FVG gap floor (pips). DERIVED choice — 0 so the self-scaling ATR gate carries the quality floor.</summary>
    private const decimal MetalFvgMinGapPips = 0m;

    /// <summary>Gold stacked-FVG proximity band (pips). INVENTED — no ICT number; 20 pips ≈ $2 between stacked gaps.</summary>
    private const decimal MetalFvgStackProximityPips = 20m;

    /// <summary>Gold relative-equal liquidity tolerance (pips). INVENTED — no ICT number; 5 pips ≈ $0.50 around equal H/L.</summary>
    private const decimal MetalEqualLevelTolerancePips = 5m;

    /// <summary>Gold EG-3 close-proximity entry band half-width (pips). INVENTED — only live under the opt-in; 5 pips ≈ $0.50.</summary>
    private const decimal MetalCloseProximityTolerancePips = 5m;

    /// <summary>Gold round-trip dealing spread (pips). CONVENTION — OANDA XAU_USD CFD ≈ 3 pips (~$0.30) round-trip.</summary>
    private const decimal MetalSpreadBasePips = 3m;

    /// <summary>Gold round-trip commission per unit. CONVENTION — OANDA XAU_USD CFD is commission-free.</summary>
    private const decimal MetalCommissionPerUnitUsd = 0m;

    private static readonly InstrumentOptionOverrides MetalOverrides = new()
    {
        MinStopDistancePips = MetalMinStopDistancePips,
        StopBufferPips = MetalStopBufferPips,
        SweptLevelExclusionPips = MetalSweptLevelExclusionPips,
        FvgMinGapPips = MetalFvgMinGapPips,
        FvgStackProximityPips = MetalFvgStackProximityPips,
        EqualLevelTolerancePips = MetalEqualLevelTolerancePips,
        CloseProximityTolerancePips = MetalCloseProximityTolerancePips,
        SpreadBasePips = MetalSpreadBasePips,
        CommissionPerLotRoundTripUsd = MetalCommissionPerUnitUsd,

        // Gold trades the FX London/NY sessions, NOT the index 08:30 AM macro — leave UseMacroOpenReference at its
        // FX default (off), so the Judas read consults only the midnight open (byte-identical to the FX path).
    };

    /// <summary>The default singleton catalog (the built-in FX majors + index CFDs + gold). Construction is cheap;
    /// this is the convenient shared instance for code paths that have no DI-injected registry.</summary>
    public static InstrumentCatalog Default { get; } = new();

    /// <summary>The built-in catalogued symbols (the FX majors + index CFDs + gold), sorted — the set the operator can
    /// pick from when adding a per-instrument override. An uncatalogued symbol still resolves (FX-default fallback), so
    /// this is a convenience list for the UI, not a hard whitelist.</summary>
    public static IReadOnlyList<string> KnownSymbols { get; } =
        FxMajors.Concat(IndexSymbols).Concat(Metals).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    public InstrumentProfile Resolve(Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        var key = symbol.Value; // Symbol normalises to trimmed upper-invariant on construction.

        if (IndexSymbols.Contains(key))
        {
            return new InstrumentProfile(
                symbol,
                SymbolSpec.Index(symbol),
                ContractSpec.Index(symbol),
                IndexOverrides,
                isKnown: true);
        }

        if (Metals.Contains(key))
        {
            // Gold: its own pip-0.1 price/money geometry (so it sizes honestly) but InstrumentClass.Fx (it hunts the
            // FX London/NY sessions), with the gold-scaled noise/cost overrides. NOT the FX-major fallback path.
            return new InstrumentProfile(
                symbol,
                SymbolSpec.Metal(symbol),
                ContractSpec.Metal(symbol),
                MetalOverrides,
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
