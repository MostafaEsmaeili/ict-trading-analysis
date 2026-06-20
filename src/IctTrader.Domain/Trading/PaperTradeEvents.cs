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
/// Raised when a paper trade closes (plan §3.0 domain events). It carries BOTH performance views (§5.3): the
/// price-based gross <see cref="RealizedR"/> (vs the original 1R — the structural edge) and the after-cost
/// <see cref="NetR"/>, plus the <see cref="GrossPnl"/>, the §5.4 <see cref="Costs"/>, and the booked
/// <see cref="NetPnl"/>. Performance reacts to this; the account books the net.
/// </summary>
public sealed record PaperTradeClosed(
    Guid TradeId,
    Guid AccountId,
    decimal RealizedR,
    decimal NetR,
    Money GrossPnl,
    Money Costs,
    Money NetPnl,
    TradeCloseReason Reason,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
