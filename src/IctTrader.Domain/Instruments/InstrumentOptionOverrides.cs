using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;

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

    /// <summary>The per-instrument "k of n" required-condition relaxation
    /// (<c>ConfluenceOptions.MinRequiredConditions</c>) — the BAKED tuning result. <c>null</c> = the strict, canonical
    /// all-required §2.5 model (FX majors). A symbol whose backtest showed a relaxed gate outperforms (NAS100 → 6 of 8,
    /// PF 1.78 vs strict 0.70) carries that k here so the LIVE scanner trades it relaxed by default, while an explicit
    /// per-run backtest override still wins. This is a TUNING value, not an ICT rule — sourced from config
    /// (<c>Ict:Instruments</c>), surfaced/overridable in appsettings, never silently magic.</summary>
    public int? MinRequiredConditions { get; init; }

    /// <summary>The per-instrument required-condition SUBSET (<c>ConfluenceOptions.RequiredConditions</c>) — the BAKED
    /// feature-subset tuning result: WHICH specific concepts to require for this symbol (the dropped ones become
    /// optional/scored). <c>null</c> = the canonical §2.5 all-required set (FX majors). The optimizer's subset search
    /// found, e.g., that NAS100 trades best WITHOUT requiring an FVG (PF 1.8 vs 0.7 strict), so the index carries the
    /// 7-condition set here. A per-run backtest subset still wins. Sourced from config (<c>Ict:Instruments</c>).</summary>
    public IReadOnlyList<ConfluenceCondition>? RequiredConditions { get; init; }

    /// <summary>The per-instrument HTF daily-bias gate (<c>DailyBiasOptions.RequireReferenceOpenAgreement</c>) — when set,
    /// this symbol additionally requires the entry price to agree with the day's reference-open bias (the web #1
    /// win-rate filter). <c>null</c> = inherit the global default (off). A TUNING value, not an ICT rule: sourced from
    /// config (<c>Ict:Instruments</c>) / the live Settings store, so an operator can require it on the pairs where a
    /// backtest shows the open-reference confluence meaningfully diverges from the gates, while keeping it off globally.</summary>
    public bool? RequireReferenceOpenAgreement { get; init; }

    /// <summary>The per-instrument Auto-vs-Manual TAKE workflow (<see cref="Configuration.PaperTradingOptions.DefaultEntryMode"/>).
    /// <c>null</c> = inherit the global default. When <see cref="Configuration.TradeEntryMode.Manual"/> a confirmed setup
    /// for THIS symbol becomes a pending opportunity the operator must TAKE (it opens nothing automatically); when
    /// <see cref="Configuration.TradeEntryMode.Auto"/> it opens automatically. A live, operator-editable preference
    /// (config <c>Ict:Instruments</c> + the revision-stamped Settings store) — paper-only either way (§6.3). NOTE: this is
    /// the WHO-acts switch, distinct from the §2.5.7 GEOMETRY scalars above; it overlays through the same merge.</summary>
    public TradeEntryMode? EntryMode { get; init; }

    /// <summary>The no-override sentinel — FX carries this so its option POCOs are returned unchanged (byte-identical).</summary>
    public static InstrumentOptionOverrides None { get; } = new();

    /// <summary>
    /// Returns this bundle with <paramref name="other"/>'s non-null fields overlaid on top — the merge the
    /// config-augmentable registry uses to apply an operator's <c>Ict:Instruments</c> overrides ON TOP of the
    /// catalog's built-in per-class values (so config wins where set, the built-in geometry survives where config is
    /// silent). A null <paramref name="other"/> field leaves this one's value unchanged.
    /// </summary>
    public InstrumentOptionOverrides OverlayWith(InstrumentOptionOverrides other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new InstrumentOptionOverrides
        {
            MinStopDistancePips = other.MinStopDistancePips ?? MinStopDistancePips,
            StopBufferPips = other.StopBufferPips ?? StopBufferPips,
            SweptLevelExclusionPips = other.SweptLevelExclusionPips ?? SweptLevelExclusionPips,
            FvgMinGapPips = other.FvgMinGapPips ?? FvgMinGapPips,
            FvgStackProximityPips = other.FvgStackProximityPips ?? FvgStackProximityPips,
            EqualLevelTolerancePips = other.EqualLevelTolerancePips ?? EqualLevelTolerancePips,
            CloseProximityTolerancePips = other.CloseProximityTolerancePips ?? CloseProximityTolerancePips,
            SpreadBasePips = other.SpreadBasePips ?? SpreadBasePips,
            CommissionPerLotRoundTripUsd = other.CommissionPerLotRoundTripUsd ?? CommissionPerLotRoundTripUsd,
            UseMacroOpenReference = other.UseMacroOpenReference ?? UseMacroOpenReference,
            MinRequiredConditions = other.MinRequiredConditions ?? MinRequiredConditions,
            RequiredConditions = other.RequiredConditions ?? RequiredConditions,
            RequireReferenceOpenAgreement = other.RequireReferenceOpenAgreement ?? RequireReferenceOpenAgreement,
            EntryMode = other.EntryMode ?? EntryMode,
        };
    }
}
