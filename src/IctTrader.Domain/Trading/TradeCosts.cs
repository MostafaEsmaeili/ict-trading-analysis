using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The broker-realistic costs of one round-trip paper trade (plan §5.4), each a non-negative cost magnitude in
/// account currency. This slice carries the two deterministic always-present FX costs — the round-trip
/// <see cref="SpreadCost"/> and the <see cref="Commission"/>; slippage and swap join as their slices land.
/// <see cref="PaperTrade.Close"/> subtracts <see cref="Total"/> from the gross P&amp;L to book the net result.
/// </summary>
public readonly record struct TradeCosts
{
    public TradeCosts(Money spreadCost, Money commission)
    {
        Guard.Against(spreadCost.IsNegative, "Spread cost cannot be negative.");
        Guard.Against(commission.IsNegative, "Commission cannot be negative.");
        SpreadCost = spreadCost;
        Commission = commission;
    }

    /// <summary>A free round trip — used where a test isolates gross price geometry, or a no-cost broker profile.</summary>
    public static readonly TradeCosts Zero = new(Money.Zero, Money.Zero);

    public Money SpreadCost { get; }

    public Money Commission { get; }

    /// <summary>The total cost deducted from gross P&amp;L to book the net result.</summary>
    public Money Total => SpreadCost + Commission;

    /// <summary>
    /// Composes two cost lines component-wise (e.g. an entry crossing folded into an exit leg, mirroring how
    /// <see cref="ExecutionCostModel.Compute"/> builds the round trip), so the round-trip leg-sum invariant holds.
    /// </summary>
    public static TradeCosts operator +(TradeCosts left, TradeCosts right)
        => new(left.SpreadCost + right.SpreadCost, left.Commission + right.Commission);
}
