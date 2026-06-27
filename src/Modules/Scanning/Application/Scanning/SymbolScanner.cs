using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// A stateful per-(symbol, style) scanner: it owns the pure-domain scan machinery — the single-symbol mutable
/// <see cref="MarketContext"/>, the PINNED canonical detector pipeline, the <see cref="SetupCandidate"/> FSM
/// (wrapped by <see cref="ScanSession"/>), and the <see cref="SetupFactory"/> — and folds one candle at a time
/// into a confirmed, priced advisory <see cref="Setup"/>. ALL decisions live in that domain; this type only
/// assembles the recipe (the exact pinned order proven by <c>ScanSessionTests</c>) and orchestrates the call.
///
/// <para><b>Single-symbol state:</b> <see cref="MarketContext"/> is mutable, single-symbol working memory, so
/// ONE instance serves ONE (symbol, style) and candles MUST be fed in chronological order — the registry never
/// shares an instance across symbols. Pure and deterministic: the same candle sequence yields the same setups
/// (the injected <see cref="TimeProvider"/> only seeds the DST-aware NY clock — there is no ambient time read).</para>
/// </summary>
public sealed class SymbolScanner
{
    private readonly ScanSession _session;
    private readonly SetupFactory _factory;
    private readonly TradeStyle _style;

    public SymbolScanner(
        Symbol symbol,
        TradeStyle style,
        TimeProvider timeProvider,
        ScannerOptions options,
        IInstrumentRegistry instruments,
        IReadOnlyList<ISetupDetector>? prependDetectors = null)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(instruments);

        Symbol = symbol;
        _style = style;

        // Per-instrument resolution (the §2.5.7 FX-vs-index split): the catalog maps the symbol to its class +
        // price geometry, and the scanner builds the MarketContext from THAT SymbolSpec — so an Index symbol
        // (NAS100USD) carries InstrumentClass.Index and the KillzoneClock routes it to ClassifyIndex (AM
        // 08:30–11:00) automatically. An FX major resolves to the existing FxMajor geometry with NO overrides, so
        // its pipeline is byte-identical to the prior hardcoded path. The index's geometry/reference re-defaults
        // are applied onto the shared options below (the FX `None` bundle is a field-equal no-op).
        var profile = instruments.Resolve(symbol);
        var resolvedOptions = options.WithInstrumentOverrides(profile.Overrides);

        var context = new MarketContext(
            profile.SymbolSpec,
            new KillzoneClock(new NyClock(timeProvider), KillzoneSchedule.CreateDefault()),
            resolvedOptions.MarketContext);

        // The PINNED canonical order (ScanSessionTests): SwingPointDetector before the MSS, and the
        // displacement feeder before the MSS, so the breach-vs-MSS race is deterministic (spec §5 item 19).
        var pipeline = new ISetupDetector[]
        {
            new SwingPointDetector(resolvedOptions.Swing),
            new LiquidityPoolDetector(resolvedOptions.Liquidity),
            new DealingRangeContextDetector(resolvedOptions.PremiumDiscount),
            new LiquiditySweepDetector(resolvedOptions.Liquidity),
            new DisplacementDetector(resolvedOptions.Displacement),
            new MarketStructureShiftDetector(resolvedOptions.MarketStructureShift),
            new FairValueGapDetector(resolvedOptions.Fvg),
            new OrderBlockDetector(resolvedOptions.OrderBlock),
            new DailyBiasDetector(resolvedOptions.DailyBias),
            new PremiumDiscountGateDetector(resolvedOptions.PremiumDiscount),
            new OteFibDetector(resolvedOptions.Ote, resolvedOptions.Fvg),
            new DrawOnLiquidityDetector(
                resolvedOptions.DrawOnLiquidity,
                resolvedOptions.Ote,
                resolvedOptions.TradeStyles,
                resolvedOptions.Fvg,
                resolvedOptions.SdProjection),
            new KillzoneEntryDetector(resolvedOptions.KillzoneEntry),
            new CalendarGateDetector(resolvedOptions.Calendar),
        };

        // TEST SEAM only: feeder/seeder detectors prepended ahead of the real pipeline so a test can seed the
        // structural events (sweep/MSS/FVG/range) the same way the proven ScanSessionTests fixture does, WITHOUT
        // hand-crafting the multi-candle sequence that would drive every real structural detector at once. The
        // production path always passes null/empty here, so the canonical pipeline is byte-identical — and the
        // pinned order for the REAL detectors (SwingPointDetector → … → MSS) is preserved regardless.
        var detectors = prependDetectors is { Count: > 0 }
            ? [.. prependDetectors, .. pipeline]
            : pipeline;

        var candidate = new SetupCandidate(
            resolvedOptions.Confluence, resolvedOptions.SetupCandidate, new SetupScorer(resolvedOptions.Confluence));
        _session = new ScanSession(context, detectors, candidate, resolvedOptions.SetupCandidate);
        _factory = new SetupFactory(resolvedOptions.TargetLadder, resolvedOptions.TradeStyles);
    }

    public Symbol Symbol { get; }

    /// <summary>The killzone classification of the most recently processed candle — read by the handler to stamp
    /// the <see cref="Domain.Setups.Setup"/>'s wire DTO with the session it confirmed in.</summary>
    public Killzone CurrentKillzone => _session.Context.Session.Killzone;

    /// <summary>Folds one candle into the scan and returns the priced advisory <see cref="Setup"/> when the FSM
    /// confirms a graded setup for this style, otherwise null. Candles must arrive in chronological order.</summary>
    public Setup? OnCandle(Candle candle)
    {
        var confirmation = _session.OnCandle(candle);
        return confirmation is null ? null : _factory.Create(confirmation, _style);
    }
}
