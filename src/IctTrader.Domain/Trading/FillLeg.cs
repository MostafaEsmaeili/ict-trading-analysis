using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// One booked exit leg of a paper trade (plan §2.5.9 partial scale-out): the lots closed, the resting LEVEL they
/// filled at, why, the §5.4 costs charged on that leg, and when. A trade's exit ledger is an ordered list of
/// these — zero or more partials plus the final leg. A leg stores only facts; the leg's R and gross/net money are
/// DERIVED by the aggregate from these facts plus its frozen geometry, so a leg can never hold a figure
/// inconsistent with its own price. Always constructed by <see cref="PaperTrade"/> after its guards, so it has no
/// self-validation of its own.
/// </summary>
public readonly record struct FillLeg(
    PositionSize Lots,
    Price ExitPrice,
    TradeCloseReason Reason,
    TradeCosts Costs,
    DateTimeOffset AtUtc);
