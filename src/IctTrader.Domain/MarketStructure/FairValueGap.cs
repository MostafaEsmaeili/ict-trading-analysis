using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.MarketStructure;

public enum FvgState
{
    Open,
    Mitigated,
    VoidedTwoTouch,
}

/// <summary>
/// A 3-candle fair-value gap (plan §2.3): bullish when c1.High &lt; c3.Low, bearish when c1.Low &gt; c3.High.
/// Rich, not anemic — it tracks its own lifecycle: two-touch void (the 3rd return voids it, Ep38) and
/// mitigation (the gap fills, so the array dies, §2.5.10). The void threshold is supplied by the caller so
/// no count is hard-coded here.
/// </summary>
public sealed class FairValueGap
{
    public FairValueGap(Direction direction, Timeframe timeframe, Price bottom, Price top, DateTimeOffset formedAtUtc)
    {
        Guard.Against(top.Value <= bottom.Value, "FairValueGap top must be above its bottom.");
        Guard.Against(formedAtUtc.Offset != TimeSpan.Zero, "FairValueGap.FormedAtUtc must be UTC.");
        Direction = direction;
        Timeframe = timeframe;
        Bottom = bottom;
        Top = top;
        FormedAtUtc = formedAtUtc;
    }

    public Direction Direction { get; }

    public Timeframe Timeframe { get; }

    public Price Bottom { get; }

    public Price Top { get; }

    public DateTimeOffset FormedAtUtc { get; }

    public FvgState State { get; private set; } = FvgState.Open;

    public int TouchCount { get; private set; }

    public bool Stacked { get; private set; }

    public bool IsSelectedEntry { get; private set; }

    public decimal Size => Top.Value - Bottom.Value;

    public decimal Midpoint => Bottom.Value + (Size / 2m);

    public bool IsOpen => State == FvgState.Open;

    /// <summary>Records a retrace into the gap; voids the array once <paramref name="voidOnTouchCount"/> is reached.</summary>
    public void RegisterTouch(int voidOnTouchCount)
    {
        Guard.Against(voidOnTouchCount < 1, "FairValueGap voidOnTouchCount must be at least 1.");
        if (!IsOpen)
        {
            return;
        }

        TouchCount++;
        if (TouchCount >= voidOnTouchCount)
        {
            State = FvgState.VoidedTwoTouch;
        }
    }

    /// <summary>Invalidation: the gap fully filled, so the array dies (§2.5.10).</summary>
    public void Mitigate()
    {
        if (IsOpen)
        {
            State = FvgState.Mitigated;
        }
    }

    public void MarkStacked() => Stacked = true;

    public void SelectAsEntry() => IsSelectedEntry = true;
}
