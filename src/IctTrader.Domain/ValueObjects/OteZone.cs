using IctTrader.Domain.Common;

namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// The Optimal Trade Entry retracement band (plan §2.4/§2.5): the 62–79% zone of the impulse leg with a
/// 70.5% sweet spot. Bounds are absolute prices already projected onto the leg. The fib is anchored
/// body-to-body by default (wicks only on FOMC/NFP — plan §2.5.10); that choice is made by the detector,
/// this value object only carries the resulting band.
/// </summary>
public readonly record struct OteZone
{
    public OteZone(Price lower, Price upper, Price sweetSpot)
    {
        Guard.Against(upper.Value < lower.Value, "OTE upper bound must be >= lower bound.");
        Guard.Against(
            sweetSpot.Value < lower.Value || sweetSpot.Value > upper.Value,
            "OTE sweet spot must lie within the band.");
        Lower = lower;
        Upper = upper;
        SweetSpot = sweetSpot;
    }

    public Price Lower { get; }

    public Price Upper { get; }

    public Price SweetSpot { get; }

    public bool Contains(Price price) => price.Value >= Lower.Value && price.Value <= Upper.Value;
}
