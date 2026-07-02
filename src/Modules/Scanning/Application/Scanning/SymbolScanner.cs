using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Application.Scanning.Models;
using IctTrader.Scanning.Contracts;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// A stateful per-(symbol, timeframe, style) scanner: it owns the pure-domain scan machinery — the single-symbol
/// mutable <see cref="MarketContext"/>, the PINNED canonical detector pipeline, the <see cref="SetupCandidate"/>
/// FSM (wrapped by <see cref="ScanSession"/>), and the <see cref="SetupFactory"/> — and folds one candle at a time
/// into a confirmed, priced advisory <see cref="Setup"/>. ALL decisions live in that domain; this type only
/// assembles the recipe (the exact pinned order proven by <c>ScanSessionTests</c>) and orchestrates the call.
///
/// <para><b>Single-symbol, single-timeframe state:</b> <see cref="MarketContext"/> is mutable, single-symbol
/// working memory, so ONE instance serves ONE (symbol, timeframe, style) and candles MUST be fed in chronological
/// order AT THIS GRANULARITY — the registry never shares an instance across keys, and a mixed-TF feed would corrupt
/// the window/FVG/MSS state. Pure and deterministic: the same candle sequence yields the same setups (the injected
/// <see cref="TimeProvider"/> only seeds the DST-aware NY clock — there is no ambient time read).</para>
/// </summary>
public sealed class SymbolScanner
{
    private readonly ScanSession _session;
    private readonly SetupFactory _factory;
    private readonly TradeStyle _style;
    private readonly Timeframe _timeframe;
    private readonly IEconomicCalendarStore? _calendarStore;

    // The resolved OB/OTE scalars the live "engine view" geometry snapshot needs — captured once so the drawn OB mean
    // line + OTE band use the SAME per-instrument config the detectors ran with (they can't drift from the entry logic).
    private readonly decimal _orderBlockMeanPercent;
    private readonly decimal _oteLowerFib;
    private readonly decimal _oteUpperFib;
    private readonly decimal _oteSweetSpotFib;

    // The store revision last loaded into this scanner's MarketContext. The store starts at revision 0 and a load
    // bumps it to >= 1, so this initial 0 forces the first real load (and only loads again on a later refresh).
    private int _loadedCalendarRevision;

    public SymbolScanner(
        Symbol symbol,
        Timeframe timeframe,
        TradeStyle style,
        TimeProvider timeProvider,
        ScannerOptions options,
        IInstrumentRegistry instruments,
        IReadOnlyList<ISetupDetector>? prependDetectors = null,
        IEconomicCalendarStore? calendarStore = null,
        SetupModelDefinition? modelDefinition = null)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(instruments);

        // The setup model this cell scans (plan §16). Null falls back to the canonical §2.5 model so every
        // pre-catalog construction site (tests, seeded fixtures) keeps its exact behavior without changes.
        var model = modelDefinition ?? Ict2022ModelDefinition.Definition;
        Model = model.Id;

        Symbol = symbol;
        _timeframe = timeframe;
        _style = style;
        _calendarStore = calendarStore;

        // Option overlay order (plan §16 D2): base snapshot → the MODEL's preset deltas → the per-instrument
        // overrides. The instrument is applied LAST — its geometry/reference corrections hold regardless of which
        // model runs. The Ict2022 preset is the identity, so the canonical path is byte-identical to pre-catalog.
        //
        // Per-instrument resolution (the §2.5.7 FX-vs-index split): the catalog maps the symbol to its class +
        // price geometry, and the scanner builds the MarketContext from THAT SymbolSpec — so an Index symbol
        // (NAS100USD) carries InstrumentClass.Index and the KillzoneClock routes it to ClassifyIndex (AM
        // 08:30–11:00) automatically. An FX major resolves to the existing FxMajor geometry with NO overrides, so
        // its pipeline is byte-identical to the prior hardcoded path. The index's geometry/reference re-defaults
        // are applied onto the shared options below (the FX `None` bundle is a field-equal no-op).
        var profile = instruments.Resolve(symbol);
        var resolvedOptions = model.ApplyPreset(options).WithInstrumentOverrides(profile.Overrides);

        _orderBlockMeanPercent = resolvedOptions.OrderBlock.MeanThresholdPercent;
        _oteLowerFib = resolvedOptions.Ote.LowerFib;
        _oteUpperFib = resolvedOptions.Ote.EffectiveUpperFib;
        _oteSweetSpotFib = resolvedOptions.Ote.SweetSpotFib;

        var nyClock = new NyClock(timeProvider);
        var context = new MarketContext(
            profile.SymbolSpec,
            new KillzoneClock(nyClock, KillzoneSchedule.CreateDefault()),
            resolvedOptions.MarketContext);

        // The model's detector pipeline, in ITS pinned canonical order (for Ict2022: the exact §2.5 recipe the
        // pre-catalog scanner hardcoded, proven by ScanSessionTests + the golden pipeline-equality test).
        var pipeline = model.BuildPipeline(resolvedOptions, nyClock);

        // TEST SEAM only: feeder/seeder detectors prepended ahead of the real pipeline so a test can seed the
        // structural events (sweep/MSS/FVG/range) the same way the proven ScanSessionTests fixture does, WITHOUT
        // hand-crafting the multi-candle sequence that would drive every real structural detector at once. The
        // production path always passes null/empty here, so the canonical pipeline is byte-identical — and the
        // pinned order for the REAL detectors (SwingPointDetector → … → MSS) is preserved regardless.
        IReadOnlyList<ISetupDetector> detectors = prependDetectors is { Count: > 0 }
            ? [.. prependDetectors, .. pipeline]
            : pipeline;

        var candidate = new SetupCandidate(
            resolvedOptions.Confluence, resolvedOptions.SetupCandidate, new SetupScorer(resolvedOptions.Confluence));
        _session = new ScanSession(context, detectors, candidate, resolvedOptions.SetupCandidate);
        _factory = new SetupFactory(resolvedOptions.TargetLadder, resolvedOptions.TradeStyles);
    }

    public Symbol Symbol { get; }

    /// <summary>The setup model this cell scans (plan §16) — the registry keys on it, so two models scanning the
    /// same (symbol, timeframe, style) hold independent FSM state and confirm independent setups.</summary>
    public SetupModel Model { get; }

    /// <summary>The granularity this cell scans (plan §4.7) — the canonical entry timeframe of its style. Every
    /// candle fed here must carry this timeframe; it stamps the confirmed <c>Setup.Timeframe</c> per cell.</summary>
    public Timeframe Timeframe => _timeframe;

    /// <summary>The killzone classification of the most recently processed candle — read by the handler to stamp
    /// the <see cref="Domain.Setups.Setup"/>'s wire DTO with the session it confirmed in.</summary>
    public Killzone CurrentKillzone => _session.Context.Session.Killzone;

    /// <summary>Folds one candle into the scan and returns the priced advisory <see cref="Setup"/> when the FSM
    /// confirms a graded setup for this style, otherwise null. Candles must arrive in chronological order and at
    /// this cell's <see cref="Timeframe"/> — a foreign granularity would corrupt the single-TF FSM state.</summary>
    public Setup? OnCandle(Candle candle)
    {
        // Defensive: this cell is single-timeframe working memory; routing a foreign granularity into it would
        // silently corrupt the window/FVG/MSS state. The registry keys by timeframe so this never trips in the live
        // loop — it fails fast if a future caller mis-routes a candle, rather than producing a wrong-TF setup.
        if (candle.Timeframe != _timeframe)
        {
            throw new ArgumentException(
                $"Candle timeframe {candle.Timeframe} does not match this scanner's {_timeframe} cell.", nameof(candle));
        }

        RefreshCalendarIfChanged();
        var confirmation = _session.OnCandle(candle);
        return confirmation is null ? null : _factory.Create(confirmation, _style, Model);
    }

    /// <summary>
    /// An immutable snapshot of the concepts this cell's <see cref="MarketContext"/> is currently tracking — the live
    /// "engine view" the ICT Pattern Chart draws under its concept toggles (plan §9.1): open FVGs / order blocks /
    /// liquidity pools, the latest sweep / MSS, and the OTE band of the latest displacement leg. PURE read of working
    /// memory; routes nowhere near an order path (§6.3). The handler captures this on the scan thread after each candle.
    /// </summary>
    public IReadOnlyList<GeometryOverlayDto> SnapshotGeometry()
        => GeometryOverlayMapper.Snapshot(
            _session.Context, _orderBlockMeanPercent, _oteLowerFib, _oteUpperFib, _oteSweetSpotFib);

    /// <summary>
    /// Loads the host's economic-calendar events into this scanner's <see cref="MarketContext"/> the first time
    /// they are sourced, and again whenever the store's revision moves (a refresh). Until a load happens the gate
    /// stays in its unverified posture (fail-open by default); once loaded, <c>CalendarGateDetector</c> withholds
    /// <c>CalendarClear</c> on a blacked-out FOMC/NFP day. No-op when no store is wired (tests / backtest).
    /// </summary>
    private void RefreshCalendarIfChanged()
    {
        if (_calendarStore is null)
        {
            return;
        }

        var revision = _calendarStore.Revision;
        if (revision == _loadedCalendarRevision)
        {
            return;
        }

        _session.Context.LoadCalendar(_calendarStore.Events);
        _loadedCalendarRevision = revision;
    }
}
