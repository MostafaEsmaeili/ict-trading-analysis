namespace IctTrader.Domain.Instruments;

/// <summary>
/// The per-instrument-class SCALAR overrides applied onto the global <c>Ict:*</c> Options POCOs when a symbol's
/// instrument class needs different geometry/cost than the FX-pip defaults (plan §2.5.7 caveat 3). The option
/// POCOs themselves stay single global instances bound + <c>ValidateOnStart</c>-gated by the Host; the catalog
/// resolves the class-appropriate values into THIS bundle, and the per-symbol construction sites apply it via the
/// POCOs' <c>WithInstrumentOverrides</c> methods. FX carries NO overrides (<see cref="None"/>), so the FX path is
/// byte-identical.
///
/// <para><b>Provenance.</b> ICT is SILENT on every numeric index threshold (the 2022 Mentorship teaches placement
/// and "1–2 ticks", never an index pip floor). So the index values below are split: a handful are DERIVED from the
/// ICT handle/tick geometry (Ep1 L214-216/L314-317), the rest are INVENTED operator-tunable noise guards that echo
/// the FX numeral as POINTS — each is flagged on its field. Nothing here is silently magic; every number is also
/// surfaceable in appsettings.</para>
///
/// <para><b>Pip semantics.</b> Because an index "pip" is one index point (<c>SymbolSpec.Nas100.PipSize = 1.0</c>),
/// these <c>*Pips</c> values read directly as POINTS. An FX-pip absolute floor (e.g. <c>FvgMinGapPips = 1.0</c>)
/// would become a wrong 1-point floor on a ~1-point index, so the ones that mis-scale are re-defaulted here.</para>
/// </summary>
public sealed record InstrumentOptionOverrides
{
    /// <summary>The minimum stop distance floor in points (<c>RiskOptions.MinStopDistancePips</c>). INVENTED — ICT
    /// gives no NQ/ES stop-distance floor; this is a noise guard that numerically echoes the FX 10-pip floor as
    /// 10 points, NOT derived from it.</summary>
    public decimal? MinStopDistancePips { get; init; }

    /// <summary>The stop buffer beyond the swept extreme in points (<c>DrawOnLiquidityOptions.StopBufferPips</c>).
    /// The RULE is ICT-derived (§2.5.5 "1–2 ticks" beyond the array); the MAGNITUDE is INVENTED — 1–2 ICT ticks is
    /// 0.25–0.5 NQ point, unrealistically tight against CFD wick noise, so 2 points is a noise-widened default.</summary>
    public decimal? StopBufferPips { get; init; }

    /// <summary>The consumed-liquidity exclusion band in points
    /// (<c>DrawOnLiquidityOptions.SweptLevelExclusionPips</c>). INVENTED — no ICT number; an FX 1.5-pip band is too
    /// tight for NAS100 equal levels that cluster within several points.</summary>
    public decimal? SweptLevelExclusionPips { get; init; }

    /// <summary>The minimum FVG gap floor in points (<c>FvgOptions.MinGapPips</c>). DERIVED choice — set to 0 for
    /// the index and lean on the §2.5.7 ATR gate (which self-scales to NAS100's larger ranges); an absolute FX
    /// 1.0-pip floor would be a meaningless 1-point floor on an index whose FVGs span tens of points.</summary>
    public decimal? FvgMinGapPips { get; init; }

    /// <summary>The stacked-FVG proximity band in points (<c>FvgOptions.StackProximityPips</c>). INVENTED — no ICT
    /// number; the FX 5-pip band is too tight for NAS100 stacked gaps. Only live under <c>StrictFirstFvg</c>.</summary>
    public decimal? FvgStackProximityPips { get; init; }

    /// <summary>The relative-equal liquidity tolerance in points
    /// (<c>LiquidityOptions.EqualLevelTolerancePips</c>). INVENTED — no ICT number; the FX 1.5-pip band is too
    /// tight for NAS100 equal highs/lows.</summary>
    public decimal? EqualLevelTolerancePips { get; init; }

    /// <summary>The EG-3 close-proximity entry-band half-width in points
    /// (<c>EntryManagementOptions.CloseProximityTolerancePips</c>). INVENTED — already provenance-flagged on the
    /// POCO; only live under the non-default <c>UseCloseProximityEntry</c>.</summary>
    public decimal? CloseProximityTolerancePips { get; init; }

    /// <summary>The round-trip dealing spread in points (<c>SpreadOptions.BasePips</c>). CONVENTION — OANDA NAS100
    /// CFD spread (~1.0 point during the active AM killzone), not an ICT number.</summary>
    public decimal? SpreadBasePips { get; init; }

    /// <summary>The round-trip commission per unit in account currency
    /// (<c>CommissionOptions.PerLotRoundTripUsd</c>). CONVENTION — OANDA NAS100 CFD is commission-free (0).</summary>
    public decimal? CommissionPerLotRoundTripUsd { get; init; }

    /// <summary>Whether the Judas reference open consults the 08:30 NY macro open
    /// (<c>MarketContextOptions.UseMacroOpenReference</c>). For the index this resolves the TIME-10 CONTESTED-~80%
    /// deferred branch ON (the index AM killzone opens at 08:30, Ep17 L154-159 dual-reference); FX stays off
    /// (byte-identical). NOT branched on <c>InstrumentClass</c> inside detector code — it is an explicit flag set
    /// here by the catalog, per TIME-10's "explicit opt-in" design.</summary>
    public bool? UseMacroOpenReference { get; init; }

    /// <summary>The no-override sentinel — FX carries this so its option POCOs are returned unchanged (byte-identical).</summary>
    public static InstrumentOptionOverrides None { get; } = new();
}
