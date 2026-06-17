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
    private readonly KillzoneClock _killzoneClock;
    private readonly MarketContextOptions _options;

    public MarketContext(SymbolSpec symbolSpec, KillzoneClock killzoneClock, MarketContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(symbolSpec);
        ArgumentNullException.ThrowIfNull(killzoneClock);
        ArgumentNullException.ThrowIfNull(options);
        SymbolSpec = symbolSpec;
        _killzoneClock = killzoneClock;
        _options = options;
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

    public IReadOnlyList<FairValueGap> OpenFvgs => _openFvgs;

    public IReadOnlyList<OrderBlock> OpenOrderBlocks => _openOrderBlocks;

    public IReadOnlyList<LiquidityPool> LiquidityPools => _liquidityPools;

    public IReadOnlyList<SwingPoint> SwingPoints => _swingPoints;

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
