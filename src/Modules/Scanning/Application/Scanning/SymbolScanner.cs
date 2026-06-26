using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
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
        IReadOnlyList<ISetupDetector>? prependDetectors = null)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        Symbol = symbol;
        _style = style;

        // FX-only scope (the methodology's default instrument class): SymbolSpec.FxMajor fixes
        // InstrumentClass.Fx, which drives the FX killzone windows (NY 07:00–10:00 / London 02:00–05:00 / …).
        // A per-instrument SymbolSpec lookup must replace this BEFORE any index symbol is scanned, or the §2.5.7
        // index killzone (AM 08:30–11:00) would silently never apply — deferred WP7 wiring.
        var context = new MarketContext(
            SymbolSpec.FxMajor(symbol),
            new KillzoneClock(new NyClock(timeProvider), KillzoneSchedule.CreateDefault()),
            options.MarketContext);

        // The PINNED canonical order (ScanSessionTests): SwingPointDetector before the MSS, and the
        // displacement feeder before the MSS, so the breach-vs-MSS race is deterministic (spec §5 item 19).
        var pipeline = new ISetupDetector[]
        {
            new SwingPointDetector(options.Swing),
            new LiquidityPoolDetector(options.Liquidity),
            new DealingRangeContextDetector(options.PremiumDiscount),
            new LiquiditySweepDetector(options.Liquidity),
            new DisplacementDetector(options.Displacement),
            new MarketStructureShiftDetector(options.MarketStructureShift),
            new FairValueGapDetector(options.Fvg),
            new OrderBlockDetector(options.OrderBlock),
            new DailyBiasDetector(options.DailyBias),
            new PremiumDiscountGateDetector(options.PremiumDiscount),
            new OteFibDetector(options.Ote, options.Fvg),
            new DrawOnLiquidityDetector(options.DrawOnLiquidity, options.Ote, options.TradeStyles, options.Fvg, options.SdProjection),
            new KillzoneEntryDetector(options.KillzoneEntry),
            new CalendarGateDetector(options.Calendar),
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
            options.Confluence, options.SetupCandidate, new SetupScorer(options.Confluence));
        _session = new ScanSession(context, detectors, candidate, options.SetupCandidate);
        _factory = new SetupFactory(options.TargetLadder, options.TradeStyles);
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
