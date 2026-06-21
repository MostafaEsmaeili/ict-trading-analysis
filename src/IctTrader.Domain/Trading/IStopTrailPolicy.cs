using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// Decides the trailed stop for an open <see cref="PaperTrade"/> from one <see cref="Candle"/> (plan §2.5.9 stop
/// management). PURE: it reads the trade's frozen geometry + the candle's price extremes only — no clock, no I/O —
/// and returns a <see cref="StopTrailDecision"/> the orchestrator applies via <see cref="PaperTrade.MoveStop"/>. The
/// DECIDE half of the trail; the aggregate ratchet is the APPLY half.
/// </summary>
public interface IStopTrailPolicy
{
    StopTrailDecision Evaluate(PaperTrade trade, Candle candle);
}
