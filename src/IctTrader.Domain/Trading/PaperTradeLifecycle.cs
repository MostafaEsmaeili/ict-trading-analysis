namespace IctTrader.Domain.Trading;

/// <summary>
/// The account/ledger flag for a paper trade (plan §5.2). It stays the minimal Open → Closed pair that
/// <see cref="PaperAccount"/> keys settlement off — a partially-scaled trade is still <c>Open</c> until the final
/// close, so the account can never settle a not-yet-final trade. The richer management state lives on
/// <see cref="TradeLifecycle"/>.
/// </summary>
public enum TradeStatus
{
    Open,
    Closed,
}

/// <summary>
/// The richer §2.5.9 trade-management state, carried ALONGSIDE <see cref="TradeStatus"/>. <c>PartialTaken</c>
/// means a T1 scale-out has booked one exit leg while the runner is still open; <c>Closed</c> always coincides
/// with <see cref="TradeStatus.Closed"/>. Breakeven-armed is deliberately NOT a value here — it is orthogonal to
/// the partial state (a trade can be both), so it is the derived <see cref="PaperTrade.IsBreakevenArmed"/> flag.
/// </summary>
public enum TradeLifecycle
{
    Open,
    PartialTaken,
    Closed,
}

/// <summary>
/// Why a paper trade closed (plan §5.2). Names align with the WP9 acceptance vocabulary. In this slice a close
/// is explicit (the caller states the reason at the exit price); the fill simulator will assign these from the
/// intrabar Open→Low→High→Close touch sequence later (WP5).
/// </summary>
public enum TradeCloseReason
{
    TargetHit,
    StopHit,
    TimeExit,
    Manual,
}
