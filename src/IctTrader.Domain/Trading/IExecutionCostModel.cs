namespace IctTrader.Domain.Trading;

/// <summary>
/// Computes the broker-realistic <see cref="TradeCosts"/> of a paper trade (plan §5.4) so its P&amp;L can be booked
/// NET. PURE: no I/O, no clock — costs depend only on the configured policy and the trade's own money geometry,
/// so a cost can never disagree with the realized P&amp;L. The model DECIDES the cost; <see cref="PaperTrade.Close"/>
/// APPLIES it. This slice prices the round-trip spread + commission; slippage and swap are follow-on slices.
/// </summary>
public interface IExecutionCostModel
{
    TradeCosts Compute(PaperTrade trade);
}
