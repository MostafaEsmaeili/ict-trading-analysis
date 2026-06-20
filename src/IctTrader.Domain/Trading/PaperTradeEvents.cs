using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// Raised when a paper trade opens (plan §3.0 domain events). The PaperTrading application layer translates it
/// into the bus <c>PaperTradeOpened</c> contract after persistence; the domain itself stays transport-free.
/// </summary>
public sealed record PaperTradeOpened(
    Guid TradeId,
    Guid AccountId,
    Symbol Symbol,
    Direction Direction,
    PositionSize Size,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

/// <summary>
/// Raised when a paper trade closes (plan §3.0 domain events), carrying the realized R (vs the original 1R)
/// and the gross realized P&amp;L. Performance reacts to this; the cost-netting (§5.4) is applied later (WP5).
/// </summary>
public sealed record PaperTradeClosed(
    Guid TradeId,
    Guid AccountId,
    decimal RealizedR,
    Money RealizedPnl,
    TradeCloseReason Reason,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
