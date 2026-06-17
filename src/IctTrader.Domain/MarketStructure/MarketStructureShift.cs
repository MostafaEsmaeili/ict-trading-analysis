using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.MarketStructure;

public enum MarketStructureShiftState
{
    Confirmed,
    Invalidated,
}

/// <summary>
/// A market-structure shift (plan §2.3/§2.5.1 step 5): an energetic displacement that breaks a prior
/// short-term swing AND closes beyond it, occurring AFTER a liquidity sweep. The "energetic + closes
/// beyond" gating is enforced by the detector; this records the confirmed shift and its ITH/ITL-breach
/// invalidation.
/// </summary>
public sealed class MarketStructureShift
{
    public MarketStructureShift(
        Direction direction,
        Timeframe timeframe,
        Price brokenSwingLevel,
        Price closeLevel,
        DateTimeOffset atUtc)
    {
        Guard.Against(atUtc.Offset != TimeSpan.Zero, "MarketStructureShift.AtUtc must be UTC.");
        Direction = direction;
        Timeframe = timeframe;
        BrokenSwingLevel = brokenSwingLevel;
        CloseLevel = closeLevel;
        AtUtc = atUtc;
    }

    public Direction Direction { get; }

    public Timeframe Timeframe { get; }

    /// <summary>The prior swing that was broken.</summary>
    public Price BrokenSwingLevel { get; }

    /// <summary>Where the displacement candle closed (must be beyond the broken swing).</summary>
    public Price CloseLevel { get; }

    public DateTimeOffset AtUtc { get; }

    public MarketStructureShiftState State { get; private set; } = MarketStructureShiftState.Confirmed;

    public bool IsConfirmed => State == MarketStructureShiftState.Confirmed;

    /// <summary>Invalidation: price closed back beyond the origin swing (ITH/ITL breach).</summary>
    public void Invalidate() => State = MarketStructureShiftState.Invalidated;
}
