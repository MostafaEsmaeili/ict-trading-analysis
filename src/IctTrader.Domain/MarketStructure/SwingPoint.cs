using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.MarketStructure;

public enum SwingKind
{
    High,
    Low,
}

public enum SwingState
{
    Active,
    Consumed,
    Breached,
}

/// <summary>
/// A fractal swing (plan §2.3/§2.5.1 step 5): a high with lower highs either side, or a low with higher
/// lows. CONTRACT (spec fix): a <see cref="SwingKind.High"/> is buy-side liquidity that enables a
/// <see cref="Direction.Bearish"/> trade; a <see cref="SwingKind.Low"/> is sell-side enabling
/// <see cref="Direction.Bullish"/> — so sweep/MSS/stop-placement all agree on the side.
/// </summary>
public sealed class SwingPoint
{
    public SwingPoint(SwingKind kind, Timeframe timeframe, Price price, DateTimeOffset formedAtUtc)
    {
        Guard.Against(formedAtUtc.Offset != TimeSpan.Zero, "SwingPoint.FormedAtUtc must be UTC.");
        Kind = kind;
        Timeframe = timeframe;
        Price = price;
        FormedAtUtc = formedAtUtc;
    }

    public SwingKind Kind { get; }

    public Timeframe Timeframe { get; }

    public Price Price { get; }

    public DateTimeOffset FormedAtUtc { get; }

    public SwingState State { get; private set; } = SwingState.Active;

    /// <summary>The candle (UTC open) that breached this swing, or null while it is still active/consumed.</summary>
    public DateTimeOffset? BreachedAtUtc { get; private set; }

    public Direction EnablesDirection => Kind == SwingKind.High ? Direction.Bearish : Direction.Bullish;

    public bool IsActive => State == SwingState.Active;

    /// <summary>Marks the swing as taken by a sweep/MSS that referenced it.</summary>
    public void MarkConsumed()
    {
        if (State == SwingState.Active)
        {
            State = SwingState.Consumed;
        }
    }

    /// <summary>
    /// Invalidation: price CLOSED beyond the swing (ITH/ITL breach) — a structural break, not a sweep. The
    /// breaching candle is stamped so the MSS detector can still claim a swing broken by the SAME displacement
    /// candle (the breach-vs-MSS ordering race, spec §5 item 19), while excluding swings breached on earlier bars.
    /// </summary>
    public void Breach(DateTimeOffset breachedAtUtc)
    {
        Guard.Against(breachedAtUtc.Offset != TimeSpan.Zero, "SwingPoint.BreachedAtUtc must be UTC.");
        State = SwingState.Breached;
        BreachedAtUtc = breachedAtUtc;
    }

    /// <summary>Whether this swing was breached by the candle opening at <paramref name="utc"/>.</summary>
    public bool WasBreachedOn(DateTimeOffset utc) => State == SwingState.Breached && BreachedAtUtc == utc;
}
