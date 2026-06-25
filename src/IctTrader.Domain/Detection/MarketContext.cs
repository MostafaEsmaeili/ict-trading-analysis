using System.Collections.ObjectModel;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection;

/// <summary>
/// The per-symbol working memory of the scanner (plan §4.1): multi-timeframe candle ring buffers, the live
/// registries of open market-structure arrays, and the current session + bias state. <see cref="Append"/>
/// is the ONLY place wall-clock state is computed (via the injected <see cref="KillzoneClock"/>), which
/// keeps the whole thing deterministic — the same candle sequence yields field-equal state, so replay
/// reproduces live exactly. Detectors read this; structural detectors register arrays through the
/// <c>Register*</c> methods.
/// </summary>
public sealed class MarketContext
{
    private readonly Dictionary<Timeframe, List<Candle>> _windows = [];
    private readonly List<FairValueGap> _openFvgs = [];
    private readonly List<OrderBlock> _openOrderBlocks = [];
    private readonly List<LiquidityPool> _liquidityPools = [];
    private readonly List<SwingPoint> _swingPoints = [];
    private readonly List<EconomicEvent> _economicEvents = [];
    private readonly ReadOnlyCollection<FairValueGap> _openFvgsView;
    private readonly ReadOnlyCollection<OrderBlock> _openOrderBlocksView;
    private readonly ReadOnlyCollection<LiquidityPool> _liquidityPoolsView;
    private readonly ReadOnlyCollection<SwingPoint> _swingPointsView;
    private readonly ReadOnlyCollection<EconomicEvent> _economicEventsView;
    private readonly KillzoneClock _killzoneClock;
    private readonly MarketContextOptions _options;
    private DateOnly? _lastNyDate;

    public MarketContext(SymbolSpec symbolSpec, KillzoneClock killzoneClock, MarketContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(symbolSpec);
        ArgumentNullException.ThrowIfNull(killzoneClock);
        ArgumentNullException.ThrowIfNull(options);
        SymbolSpec = symbolSpec;
        _killzoneClock = killzoneClock;
        _options = options;

        // Expose read-only WRAPPERS over the live backing lists so callers cannot cast and mutate them —
        // the Register*/LoadCalendar methods stay the single controlled mutation path.
        _openFvgsView = _openFvgs.AsReadOnly();
        _openOrderBlocksView = _openOrderBlocks.AsReadOnly();
        _liquidityPoolsView = _liquidityPools.AsReadOnly();
        _swingPointsView = _swingPoints.AsReadOnly();
        _economicEventsView = _economicEvents.AsReadOnly();
    }

    public SymbolSpec SymbolSpec { get; }

    public Symbol Symbol => SymbolSpec.Symbol;

    public InstrumentClass InstrumentClass => SymbolSpec.InstrumentClass;

    /// <summary>The killzone classification of the most recently appended candle.</summary>
    public KillzoneClassification Session { get; private set; } = KillzoneClassification.None;

    /// <summary>The current daily bias; null means NEUTRAL (no trade).</summary>
    public Direction? Bias { get; private set; }

    public DealingRange? DailyRange { get; private set; }

    /// <summary>The most recent displacement leg — its 50% is the premium/discount anchor the FVG half-gate uses.</summary>
    public Displacement? LastDisplacement { get; private set; }

    /// <summary>The most recent liquidity sweep — the MSS detector requires one within its bar window.</summary>
    public SweepRecord? LastSweep { get; private set; }

    /// <summary>The most recent confirmed market-structure shift.</summary>
    public MarketStructureShift? LastMss { get; private set; }

    /// <summary>Monotonic count of candles appended — used for bar-distance windows (sweep→MSS).</summary>
    public long BarsProcessed { get; private set; }

    /// <summary>The open of the first candle of the current New-York day (00:00 NY reference for the Judas read).</summary>
    public decimal? MidnightOpen { get; private set; }

    /// <summary>
    /// The open of the first candle at/after the configured macro reference time (08:30 NY by default) of the
    /// current New-York day — the secondary Judas reference for FX New-York-session trades (TIME-10 / Ep17
    /// L154-159). Null before the macro time, and all day if no candle reaches it; reset on the NY-day rollover.
    /// </summary>
    public decimal? MacroOpen { get; private set; }

    /// <summary>The New-York calendar date of the most recently appended candle (the financial day, 00:00 NY).</summary>
    public DateOnly? CurrentNewYorkDate => _lastNyDate;

    /// <summary>Whether the economic calendar has been loaded — distinguishes "no events" from "data not yet supplied".</summary>
    public bool IsCalendarLoaded { get; private set; }

    /// <summary>The scheduled economic events the calendar gate reads (sourced by the host/ingestion).</summary>
    public IReadOnlyList<EconomicEvent> EconomicEvents => _economicEventsView;

    public IReadOnlyList<FairValueGap> OpenFvgs => _openFvgsView;

    public IReadOnlyList<OrderBlock> OpenOrderBlocks => _openOrderBlocksView;

    public IReadOnlyList<LiquidityPool> LiquidityPools => _liquidityPoolsView;

    public IReadOnlyList<SwingPoint> SwingPoints => _swingPointsView;

    /// <summary>The candle ring buffer for a timeframe — oldest at index 0, newest at <c>[^1]</c> (plan §4.3).</summary>
    public IReadOnlyList<Candle> Window(Timeframe timeframe)
        => _windows.TryGetValue(timeframe, out var window) ? window : [];

    /// <summary>Appends a candle: updates its timeframe window, recomputes the session, and prunes dead arrays.</summary>
    public void Append(Candle candle)
    {
        Guard.Against(candle.Symbol != Symbol, "Candle symbol does not match this MarketContext.");

        var window = GetOrCreateWindow(candle.Timeframe);
        window.Add(candle);
        if (window.Count > _options.WindowCapacity)
        {
            window.RemoveAt(0);
        }

        BarsProcessed++;
        TrackNewYorkDay(candle);
        CaptureMacroOpen(candle);
        Session = _killzoneClock.Classify(candle.OpenTimeUtc, InstrumentClass);
        PruneDeadArrays();
    }

    public void RegisterFvg(FairValueGap fvg) => Register(_openFvgs, fvg);

    public void RegisterOrderBlock(OrderBlock orderBlock) => Register(_openOrderBlocks, orderBlock);

    public void RegisterLiquidityPool(LiquidityPool pool) => Register(_liquidityPools, pool);

    public void RegisterSwingPoint(SwingPoint swing) => Register(_swingPoints, swing);

    public void SetBias(Direction? bias) => Bias = bias;

    public void SetDailyRange(DealingRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        DailyRange = range;
    }

    public void SetDisplacement(Displacement displacement)
    {
        ArgumentNullException.ThrowIfNull(displacement);
        LastDisplacement = displacement;
    }

    public void SetSweep(SweepRecord sweep) => LastSweep = sweep;

    public void SetMarketStructureShift(MarketStructureShift shift)
    {
        ArgumentNullException.ThrowIfNull(shift);
        LastMss = shift;
    }

    /// <summary>Loads the economic calendar (replacing any prior set) and marks it available to the gate.</summary>
    public void LoadCalendar(IEnumerable<EconomicEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        _economicEvents.Clear();
        _economicEvents.AddRange(events);
        IsCalendarLoaded = true;
    }

    /// <summary>
    /// Resolves the reference open the Judas read tests the swept wick against (TIME-10). FX default
    /// (<see cref="MarketContextOptions.UseMacroOpenReference"/> off) is the midnight open — byte-identical to
    /// the prior behaviour. With the flag on, once BOTH opens exist (after the macro time) a BEARISH read
    /// (premium) takes the LOWER open and a BULLISH read the HIGHER (Ep17 L154-159); before the macro time
    /// only the midnight open exists, so it falls back to whichever single open is available.
    /// </summary>
    public decimal? ReferenceOpen(bool premium)
    {
        if (!_options.UseMacroOpenReference)
        {
            return MidnightOpen;
        }

        if (MidnightOpen is not { } midnight)
        {
            return MacroOpen;
        }

        if (MacroOpen is not { } macro)
        {
            return midnight;
        }

        // BEARISH (premium) wants the lowest "minimum threshold" above which a rally is the Judas to the
        // upside; BULLISH is the literal reverse (Ep17 "everything reverse").
        return premium ? Math.Min(midnight, macro) : Math.Max(midnight, macro);
    }

    private void CaptureMacroOpen(Candle candle)
    {
        // Capture once per NY day on the first candle whose NY open time is at/after the macro reference time
        // (inclusive at 08:30, consistent with the inclusive-start session windows). NY math via the killzone
        // clock only — never the host zone / DateTime.Now.
        if (MacroOpen is not null)
        {
            return;
        }

        var nyTime = _killzoneClock.NewYorkTimeOfDay(candle.OpenTimeUtc);
        if (nyTime >= _options.MacroReferenceOpenTime)
        {
            MacroOpen = candle.Open;
        }
    }

    private void TrackNewYorkDay(Candle candle)
    {
        var nyDate = _killzoneClock.NewYorkDate(candle.OpenTimeUtc);
        if (_lastNyDate == nyDate)
        {
            return;
        }

        // The first candle initialises the day — that is NOT a rollover, so it must not clear state.
        var crossedNyMidnight = _lastNyDate is not null;
        _lastNyDate = nyDate;
        MidnightOpen = candle.Open;

        // The macro open is per-NY-day: clear it on both the first-candle init and a genuine rollover so a
        // new day starts looking for its own 08:30 capture (CaptureMacroOpen re-sets it on this very candle
        // when it already sits at/after the macro time — the first-candle-08:30 case).
        MacroOpen = null;

        // 00:00 NY is the financial-day boundary (plan §2.1/§4.8): intraday session-scoped state must not
        // bleed across days when the operator enables the reset.
        if (crossedNyMidnight && _options.ResetSessionStateAtNyMidnight)
        {
            Bias = null;
            DailyRange = null;
            LastDisplacement = null;
            LastSweep = null;
            LastMss = null;
        }
    }

    private List<Candle> GetOrCreateWindow(Timeframe timeframe)
    {
        if (!_windows.TryGetValue(timeframe, out var window))
        {
            window = [];
            _windows[timeframe] = window;
        }

        return window;
    }

    private void Register<T>(List<T> registry, T array)
    {
        ArgumentNullException.ThrowIfNull(array);
        registry.Add(array);
        while (registry.Count > _options.MaxOpenArraysPerType)
        {
            registry.RemoveAt(0);
        }
    }

    private void PruneDeadArrays()
    {
        _openFvgs.RemoveAll(fvg => !fvg.IsOpen);
        _openOrderBlocks.RemoveAll(orderBlock => !orderBlock.IsOpen);
        _liquidityPools.RemoveAll(pool => !pool.Untapped);
        _swingPoints.RemoveAll(swing => swing.State == SwingState.Breached);
    }
}
