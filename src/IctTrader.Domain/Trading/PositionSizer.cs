using IctTrader.Domain.Common;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The sized outcome of <see cref="PositionSizer"/>: the floored <see cref="Size"/> in lots, the actual money
/// at risk if the stop is hit (<see cref="RiskBudget"/> — at-or-below the nominal budget because the lots are
/// floored to the lot step), and the stop distance in pips. The risk budget is the trade's true exposure, so
/// the account books open-risk and the realized stop-out P&amp;L against exactly this number.
/// </summary>
public readonly record struct PositionSizing(PositionSize Size, Money RiskBudget, Pips StopDistance);

/// <summary>
/// The pure §5.1 position-sizing chain. From equity, the effective risk percent, the priced plan, and the
/// instrument's price + money geometry it derives the lot size:
/// <code>
/// riskPerUnit = |entry - stop|
/// stopPips    = riskPerUnit / pipSize
/// riskAmount  = equity * effRisk% / 100
/// lots        = floor( riskAmount / (stopPips * valuePerPip) / lotStep ) * lotStep
/// </code>
/// Rounding is always DOWN to the lot step so realized risk never exceeds the budget. Guards: the stop must
/// clear the configured minimum distance, and the floored size must reach the minimum lot — otherwise the
/// equity is too small to take the trade and a <see cref="DomainException"/> is thrown (never a 0-lot trade).
/// </summary>
public static class PositionSizer
{
    public static PositionSizing Size(
        Money equity,
        RiskPercent risk,
        TradePlan plan,
        SymbolSpec symbolSpec,
        ContractSpec contractSpec,
        Pips minStopDistance)
    {
        ArgumentNullException.ThrowIfNull(symbolSpec);
        ArgumentNullException.ThrowIfNull(contractSpec);
        Guard.Against(!equity.IsPositive, "Position sizing requires positive equity.");

        var riskPerUnit = Math.Abs(plan.Entry.Value - plan.Stop.Value);
        var stopPips = symbolSpec.PriceToPips(riskPerUnit);
        Guard.Against(
            stopPips.Value < minStopDistance.Value,
            $"Stop distance {stopPips} is below the minimum {minStopDistance}.");

        var riskAmount = equity.Amount * risk.Value / 100m;
        var moneyPerLot = stopPips.Value * contractSpec.ValuePerPip;
        var rawLots = riskAmount / moneyPerLot;
        var lots = Math.Floor(rawLots / contractSpec.LotStep) * contractSpec.LotStep;
        Guard.Against(
            lots < contractSpec.MinLot,
            $"Equity {equity} risks too little at {risk} to meet the minimum lot {contractSpec.MinLot}.");

        var actualRisk = new Money(lots * moneyPerLot);
        return new PositionSizing(new PositionSize(lots), actualRisk, stopPips);
    }
}
