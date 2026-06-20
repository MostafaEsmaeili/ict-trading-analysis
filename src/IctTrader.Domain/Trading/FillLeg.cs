using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// One booked exit leg of a paper trade (plan §2.5.9 partial scale-out): the lots closed, the resting LEVEL they
/// filled at, why, the §5.4 costs charged on that leg, and when. A trade's exit ledger is an ordered list of
/// these — zero or more partials plus the final leg. A leg stores only facts; the leg's R and gross/net money are
/// DERIVED by the aggregate from these facts plus its frozen geometry, so a leg can never hold a figure
/// inconsistent with its own price. It self-validates (UTC time + the already-self-validating <see cref="Lots"/>,
/// <see cref="ExitPrice"/>, <see cref="Costs"/> value objects) so it cannot be constructed in an invalid state even
/// outside the aggregate.
/// </summary>
public readonly record struct FillLeg
{
    public FillLeg(PositionSize lots, Price exitPrice, TradeCloseReason reason, TradeCosts costs, DateTimeOffset atUtc)
    {
        Guard.Against(atUtc.Offset != TimeSpan.Zero, "FillLeg.AtUtc must be UTC.");

        Lots = lots;
        ExitPrice = exitPrice;
        Reason = reason;
        Costs = costs;
        AtUtc = atUtc;
    }

    public PositionSize Lots { get; }

    public Price ExitPrice { get; }

    public TradeCloseReason Reason { get; }

    public TradeCosts Costs { get; }

    public DateTimeOffset AtUtc { get; }
}
