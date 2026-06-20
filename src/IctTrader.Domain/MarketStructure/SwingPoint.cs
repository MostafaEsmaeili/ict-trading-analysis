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

    /// <summary>Invalidation: price CLOSED beyond the swing (ITH/ITL breach) — a structural break, not a sweep.</summary>
    public void Breach() => State = SwingState.Breached;
}
