using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.MarketStructure;

/// <summary>Where a price sits in a dealing range relative to its 50% equilibrium (plan §2.4/§2.5.1 step 1/6).</summary>
public enum PremiumDiscount
{
    Discount,
    Equilibrium,
    Premium,
}

/// <summary>
/// The single, shared definition of the premium/discount boundary (plan §2.5.1 step 1/6; bias verdict fix 3).
/// Both <c>DailyBiasDetector</c> and <c>PremiumDiscountGateDetector</c> consume this so the exactly-at-50%
/// semantics are defined ONCE: a position below the equilibrium is discount, above is premium, and exactly on
/// it is the equilibrium boundary (neutral for bias; inclusive-to-both-sides for the entry gate).
/// </summary>
public static class EquilibriumBoundaryPolicy
{
    /// <summary>The ICT equilibrium is the 50% of the dealing range — a semantic invariant, not a tuning knob.</summary>
    public const decimal IctEquilibriumPercent = 0.50m;

    /// <summary>Classifies a 0..100 position-percent against a 0..100 equilibrium percent.</summary>
    public static PremiumDiscount Classify(decimal positionPercent, decimal equilibriumPercent)
    {
        if (positionPercent < equilibriumPercent)
        {
            return PremiumDiscount.Discount;
        }

        return positionPercent > equilibriumPercent ? PremiumDiscount.Premium : PremiumDiscount.Equilibrium;
    }

    /// <summary>The bias a half implies: discount ⇒ bullish, premium ⇒ bearish, equilibrium ⇒ null (NEUTRAL, no trade).</summary>
    public static Direction? BiasFor(PremiumDiscount half) => half switch
    {
        PremiumDiscount.Discount => Direction.Bullish,
        PremiumDiscount.Premium => Direction.Bearish,
        _ => null,
    };

    /// <summary>
    /// The entry-half veto (§2.5.1 step 6 — never sell in discount / buy in premium): a long is allowed only in
    /// discount, a short only in premium; exactly-at-equilibrium is allowed both sides iff
    /// <paramref name="inclusiveAtEquilibrium"/>.
    /// </summary>
    public static bool Allows(Direction direction, PremiumDiscount half, bool inclusiveAtEquilibrium)
    {
        if (half == PremiumDiscount.Equilibrium)
        {
            return inclusiveAtEquilibrium;
        }

        return direction == Direction.Bullish
            ? half == PremiumDiscount.Discount
            : half == PremiumDiscount.Premium;
    }
}
