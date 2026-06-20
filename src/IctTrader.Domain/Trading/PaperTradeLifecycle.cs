namespace IctTrader.Domain.Trading;

/// <summary>
/// The paper-trade lifecycle state (plan §5.2). This slice models the minimal Open → Closed path; the
/// realistic <c>Pending</c> (entry-touch) state and intrabar fills arrive with the fill simulator (WP5).
/// </summary>
public enum TradeStatus
{
    Open,
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
