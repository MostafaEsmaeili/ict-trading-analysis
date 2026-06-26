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
    /// <summary>
    /// A single-candle leg — the legacy overload kept so the existing call sites need no churn. Delegates with
    /// <c>originAtUtc == atUtc</c> and <c>legBars == 1</c> (TIME-11-12), so a one-bar leg is byte-identical.
    /// </summary>
    public Displacement(Direction direction, Timeframe timeframe, Price origin, Price terminus, DateTimeOffset atUtc)
        : this(direction, timeframe, origin, terminus, atUtc, originAtUtc: atUtc, legBars: 1)
    {
    }

    /// <summary>
    /// A possibly multi-candle leg (TIME-11-12): <paramref name="originAtUtc"/> is the run's first member open,
    /// <paramref name="atUtc"/> the terminus (birth) bar open, and <paramref name="legBars"/> the member count.
    /// </summary>
    public Displacement(
        Direction direction,
        Timeframe timeframe,
        Price origin,
        Price terminus,
        DateTimeOffset atUtc,
        DateTimeOffset originAtUtc,
        int legBars)
    {
        Guard.Against(atUtc.Offset != TimeSpan.Zero, "Displacement.AtUtc must be UTC.");
        Guard.Against(originAtUtc.Offset != TimeSpan.Zero, "Displacement.OriginAtUtc must be UTC.");
        Guard.Against(originAtUtc > atUtc, "Displacement origin must not be after terminus.");
        Guard.Against(legBars < 1, "Displacement.LegBars must be at least 1.");
        Direction = direction;
        Timeframe = timeframe;
        Origin = origin;
        Terminus = terminus;
        AtUtc = atUtc;
        OriginAtUtc = originAtUtc;
        LegBars = legBars;
    }

    public Direction Direction { get; }

    public Timeframe Timeframe { get; }

    /// <summary>Where the leg began.</summary>
    public Price Origin { get; }

    /// <summary>The leg's extreme (where the displacement reached).</summary>
    public Price Terminus { get; }

    /// <summary>The terminus (birth) bar open — the confirming candle of the leg.</summary>
    public DateTimeOffset AtUtc { get; }

    /// <summary>The open of the run's first member candle (== <see cref="AtUtc"/> for a single-bar leg).</summary>
    public DateTimeOffset OriginAtUtc { get; }

    /// <summary>The number of candles in the run, 1..DisplacementLegMaxBars.</summary>
    public int LegBars { get; }

    public decimal Size => Math.Abs(Terminus.Value - Origin.Value);

    /// <summary>The 50% of the displacement leg — the premium/discount split for the entry-half gate (§2.5.1 step 6).</summary>
    public decimal EquilibriumPrice => Math.Min(Origin.Value, Terminus.Value) + (Size / 2m);

    /// <summary>Project a price on the leg's fib axis: <paramref name="fraction"/> 0 = terminus, 1 = origin; NEGATIVE
    /// fractions EXTEND beyond the terminus in the draw direction (the SD / target-projection axis). This is the SINGLE
    /// shared axis the OTE entry retrace (TGR-2) and the standard-deviation targets both use, so they can never drift.</summary>
    public decimal Project(decimal fraction) => Terminus.Value + (fraction * (Origin.Value - Terminus.Value));

    public bool Retraced { get; private set; }

    /// <summary>Invalidation: price fully retraced the leg.</summary>
    public void MarkRetraced() => Retraced = true;
}
