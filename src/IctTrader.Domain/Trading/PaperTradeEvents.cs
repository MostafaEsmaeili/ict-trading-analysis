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

/// <summary>
/// Raised when a paper trade takes a partial scale-out (plan §2.5.9) — one exit leg booked while the runner stays
/// open. Carries the DERIVED per-leg figures (the leg's price-R, its gross/cost/net money, the size fraction and
/// the remaining size) so Performance and the dashboard can reconstruct the blend without the leg storing them.
/// No account settlement happens here — the money lands on equity in the single terminal
/// <see cref="PaperTradeClosed"/>.
/// </summary>
public sealed record PaperTradePartialClosed(
    Guid TradeId,
    Guid AccountId,
    decimal LegR,
    Money LegGross,
    Money LegCosts,
    Money LegNet,
    decimal Fraction,
    PositionSize LegSize,
    PositionSize RemainingSize,
    TradeCloseReason Reason,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

/// <summary>
/// Raised when a paper trade ratchets its stop toward profit (plan §2.5.9). Carries the previous and new stop and a
/// snapshot of whether the stop is now at-or-beyond breakeven, so Alerting/the dashboard need not recompute. A stop
/// move books no money — the frozen risk and R denominator are unchanged.
/// </summary>
public sealed record PaperTradeStopMoved(
    Guid TradeId,
    Guid AccountId,
    Price PreviousStop,
    Price NewStop,
    bool BreakevenArmed,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

/// <summary>
/// Raised when a confirmed advisory setup is ARMED as a resting limit (plan §2.5.1 step 7): its risk is now reserved
/// against the account's portfolio cap while it waits for the price retrace. The armed entry's id becomes the trade
/// id if it triggers, so subscribers can correlate this with the eventual <see cref="PaperTradeOpened"/>.
/// </summary>
public sealed record EntryArmed(
    Guid ArmedEntryId,
    Guid AccountId,
    Symbol Symbol,
    Direction Direction,
    PositionSize Size,
    Money RiskBudget,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

/// <summary>
/// Raised when a resting <see cref="ArmedEntry"/> is TRIGGERED by the entry touch (plan §2.5.1 step 7) — it opens the
/// <see cref="PaperTrade"/> that carries the SAME id (a key re-label), so the reserved risk transfers to that trade
/// without a second reservation. <see cref="PaperTradeOpened"/> follows on the trade itself.
/// </summary>
public sealed record EntryTriggered(
    Guid ArmedEntryId,
    Guid AccountId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
