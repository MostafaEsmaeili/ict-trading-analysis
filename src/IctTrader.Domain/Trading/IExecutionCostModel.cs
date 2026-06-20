using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// Computes the broker-realistic <see cref="TradeCosts"/> of a paper trade (plan §5.4) so its P&amp;L can be booked
/// NET. PURE: no I/O, no clock — costs depend only on the configured policy and the trade's own money geometry,
/// so a cost can never disagree with the realized P&amp;L. The model DECIDES the cost; <see cref="PaperTrade.Close"/>
/// APPLIES it. This slice prices the round-trip spread + commission; slippage and swap are follow-on slices.
/// </summary>
public interface IExecutionCostModel
{
    /// <summary>The full round-trip cost (one entry crossing + one full-size exit) — the no-partial path.</summary>
    TradeCosts Compute(PaperTrade trade);

    /// <summary>
    /// The ENTRY-leg cost: one spread crossing on the full position, NO commission (commission is a round-turn
    /// charge levied across the exit leg(s)). Charged once per trade.
    /// </summary>
    TradeCosts ComputeEntryLeg(PaperTrade trade);

    /// <summary>
    /// The cost of ONE exit leg of <paramref name="legSize"/> lots: one spread crossing on those lots plus the
    /// round-turn commission for those lots. The exit legs of a trade sum back to exactly one full crossing, so a
    /// partial + a runner never double-count the round-trip spread the no-partial <see cref="Compute"/> charges.
    /// </summary>
    TradeCosts ComputeExitLeg(PaperTrade trade, PositionSize legSize);
}
