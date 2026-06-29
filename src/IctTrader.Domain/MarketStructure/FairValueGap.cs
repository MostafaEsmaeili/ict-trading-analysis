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

    /// <summary>
    /// The ICT <em>consequent encroachment</em> (CE) of the gap — its 50% midpoint, <c>(High + Low) / 2</c>. CE is
    /// the canonical SHALLOW entry inside a fair-value gap: price reaches the gap's 50% more often than the deep
    /// 62–79% OTE retrace, so it converts more setups into real fills (a slightly worse entry, no look-ahead). CE
    /// equals <see cref="Midpoint"/> by construction; it is a distinct, named accessor so the entry-zone intent is
    /// explicit at the call site. Provenance: CE is ICT-canon ("consequent encroachment") but Primer/community vs
    /// the §2.5 70.5% deep-OTE default.
    /// </summary>
    public decimal ConsequentEncroachment => (Top.Value + Bottom.Value) / 2m;

    /// <summary>
    /// The NEAR (proximal) edge of the gap — the first level price taps as it retraces in: a BULLISH gap's
    /// <see cref="Top"/> (price retraces DOWN into it) or a BEARISH gap's <see cref="Bottom"/> (retraces UP). It is
    /// the SHALLOWEST entry inside the gap (shallower than <see cref="ConsequentEncroachment"/>/<see cref="Midpoint"/>),
    /// so a resting limit here fills the most often at the worst entry price — the §2.5.1-step-7 "tap the gap" backup.
    /// </summary>
    public decimal NearEdge => Direction == Direction.Bullish ? Top.Value : Bottom.Value;

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

    /// <summary>FVG-SEM-2a stacked DETECTION: a deeper same-direction gap sits within the stack proximity (Ep3).</summary>
    public void MarkStacked() => Stacked = true;

    /// <summary>FVG-SEM-2a: marks this gap as the resolved §2.5.1-step-7 entry FVG (single writer = the OTE detector).</summary>
    public void SelectAsEntry() => IsSelectedEntry = true;

    /// <summary>
    /// FVG-SEM-2a stale-mark teardown: clears the entry selection + stacked flags so a mark from a prior
    /// displacement leg never survives into the next (the clean half of the detector's clean-then-set marking,
    /// and the per-leg reset in <c>MarketContext.SetDisplacement</c>).
    /// </summary>
    public void ClearEntrySelection()
    {
        IsSelectedEntry = false;
        Stacked = false;
    }
}
