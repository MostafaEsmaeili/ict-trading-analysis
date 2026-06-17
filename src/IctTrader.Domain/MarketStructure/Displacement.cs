using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.MarketStructure;

/// <summary>
/// An energetic displacement leg (plan §2.3/§2.5.1 step 5) — the impulse that creates the FVG and breaks
/// structure. Its 50% is the equilibrium the premium/discount entry gate is measured against (the FVG
/// correct-half check). The energy quantification (body/ATR/close-beyond) lives in the detector; this
/// type carries the resulting leg and its retrace invalidation.
/// </summary>
public sealed class Displacement
{
    public Displacement(Direction direction, Timeframe timeframe, Price origin, Price terminus, DateTimeOffset atUtc)
    {
        Guard.Against(atUtc.Offset != TimeSpan.Zero, "Displacement.AtUtc must be UTC.");
        Direction = direction;
        Timeframe = timeframe;
        Origin = origin;
        Terminus = terminus;
        AtUtc = atUtc;
    }

    public Direction Direction { get; }

    public Timeframe Timeframe { get; }

    /// <summary>Where the leg began.</summary>
    public Price Origin { get; }

    /// <summary>The leg's extreme (where the displacement reached).</summary>
    public Price Terminus { get; }

    public DateTimeOffset AtUtc { get; }

    public decimal Size => Math.Abs(Terminus.Value - Origin.Value);

    /// <summary>The 50% of the displacement leg — the premium/discount split for the entry-half gate (§2.5.1 step 6).</summary>
    public decimal EquilibriumPrice => Math.Min(Origin.Value, Terminus.Value) + (Size / 2m);

    public bool Retraced { get; private set; }

    /// <summary>Invalidation: price fully retraced the leg.</summary>
    public void MarkRetraced() => Retraced = true;
}
