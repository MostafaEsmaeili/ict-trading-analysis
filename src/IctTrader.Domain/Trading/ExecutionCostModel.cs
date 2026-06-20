using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The pure §5.4 execution-cost model (this slice: round-trip spread + commission). It reads the trade's OWN money
/// geometry (<see cref="PaperTrade.ValuePerPipForPosition"/> and <see cref="PaperTrade.Size"/>) rather than
/// re-deriving from a <see cref="ContractSpec"/>, so the cost it computes can never disagree with the P&amp;L the
/// trade books. Spread is charged on BOTH legs of the round trip (§5.4 "entries cross the spread"); commission is a
/// round-turn per-lot charge (both legs already in the per-lot figure), levied once.
/// </summary>
public sealed class ExecutionCostModel : IExecutionCostModel
{
    private const decimal RoundTripLegs = 2m;

    private readonly ExecutionCostOptions _options;

    public ExecutionCostModel(ExecutionCostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public TradeCosts Compute(PaperTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        // Round-trip spread: one crossing on entry + one on exit. ValuePerPipForPosition already folds in the lots,
        // so this is the money cost of crossing BasePips twice for the whole position.
        var spreadCost = new Money(RoundTripLegs * _options.Spread.BasePips * trade.ValuePerPipForPosition);

        // Commission is round-turn per lot (both legs baked into the per-lot figure), charged once for the position.
        var commission = new Money(_options.Commission.PerLotRoundTripUsd * trade.Size.Lots);

        return new TradeCosts(spreadCost, commission);
    }
}
