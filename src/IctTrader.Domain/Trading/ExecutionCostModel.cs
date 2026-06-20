using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The pure §5.4 execution-cost model (this slice: spread + commission). It reads the trade's OWN money geometry
/// (<see cref="PaperTrade.ValuePerPipForPosition"/> and <see cref="PaperTrade.Size"/>) rather than re-deriving from
/// a <see cref="ContractSpec"/>, so the cost it computes can never disagree with the P&amp;L the trade books. The
/// round trip is split into one ENTRY crossing plus one EXIT crossing per leg, so a partial scale-out + a runner
/// pay exactly the same total spread as one full exit (§5.4 "entries cross the spread"); commission is a round-turn
/// per-lot charge (both legs already in the per-lot figure), amortized across the exit leg(s).
/// </summary>
public sealed class ExecutionCostModel : IExecutionCostModel
{
    private readonly ExecutionCostOptions _options;

    public ExecutionCostModel(ExecutionCostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public TradeCosts Compute(PaperTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        // The no-partial round trip = the single entry crossing + one full-size exit crossing. Built from the leg
        // primitives so the round-trip total can never drift from the per-leg costs a scaled trade pays.
        var entry = ComputeEntryLeg(trade);
        var exit = ComputeExitLeg(trade, trade.Size);
        return new TradeCosts(entry.SpreadCost + exit.SpreadCost, entry.Commission + exit.Commission);
    }

    public TradeCosts ComputeEntryLeg(PaperTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        // One spread crossing on the whole position; commission is round-turn and is levied across the exit leg(s).
        var entrySpread = new Money(_options.Spread.BasePips * trade.ValuePerPipForPosition);
        return new TradeCosts(entrySpread, Money.Zero);
    }

    public TradeCosts ComputeExitLeg(PaperTrade trade, PositionSize legSize)
    {
        ArgumentNullException.ThrowIfNull(trade);
        Guard.Against(
            legSize.Lots > trade.Size.Lots,
            $"Exit leg lots {legSize.Lots} cannot exceed the trade size {trade.Size.Lots}.");

        // One spread crossing on THIS leg's lots + the round-turn commission for those lots. value-per-pip per lot
        // is the position value over the original size, so the exit legs sum back to exactly one full crossing.
        var valuePerPipPerLot = trade.ValuePerPipForPosition / trade.Size.Lots;
        var exitSpread = new Money(_options.Spread.BasePips * valuePerPipPerLot * legSize.Lots);
        var commission = new Money(_options.Commission.PerLotRoundTripUsd * legSize.Lots);
        return new TradeCosts(exitSpread, commission);
    }
}
