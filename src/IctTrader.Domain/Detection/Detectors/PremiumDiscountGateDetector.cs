using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// The sole owner of <see cref="ConfluenceCondition.PremiumDiscountHalf"/> (0.85, required) — the hard entry-half
/// veto (plan §2.5.1 step 6, §2.5.10): never sell in discount, never buy in premium. Using the shared
/// <see cref="EquilibriumBoundaryPolicy"/> over the dealing range, it emits the half-allowed direction (discount ⇒
/// long, premium ⇒ short); exactly-at-equilibrium emits a non-directional match when inclusive (allowed both
/// sides). The confluence FSM realises the veto by counting the condition only when the emitted direction does not
/// contradict the candidate's locked direction.
/// </summary>
public sealed class PremiumDiscountGateDetector : ISetupDetector
{
    private readonly PremiumDiscountOptions _options;

    public PremiumDiscountGateDetector(PremiumDiscountOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.PremiumDiscountHalf;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.DailyRange is not { } range)
        {
            return DetectorResult.NoMatch; // no dealing range -> no half to gate on
        }

        var positionPercent = range.PositionPercent(new Price(current.Close));
        var half = EquilibriumBoundaryPolicy.Classify(positionPercent, _options.EquilibriumPercent * 100m);

        if (half == PremiumDiscount.Equilibrium && !_options.InclusiveAtEquilibrium)
        {
            return DetectorResult.NoMatch; // exactly on the boundary and not inclusive
        }

        var allowed = half switch
        {
            PremiumDiscount.Discount => (Direction?)Direction.Bullish, // longs only in discount
            PremiumDiscount.Premium => Direction.Bearish,              // shorts only in premium
            _ => null,                                                 // equilibrium (inclusive): both sides
        };

        var equilibrium = range.Equilibrium(_options.EquilibriumPercent);
        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.PositionPercent] = positionPercent,
            [EvidenceKeys.EquilibriumPrice] = equilibrium,
        };
        if (allowed is { } direction)
        {
            evidence[EvidenceKeys.Direction] = direction.ToString();
        }

        return DetectorResult.Match(allowed, equilibrium, ReasonFragments.PremiumDiscountHalf(half), evidence);
    }
}
