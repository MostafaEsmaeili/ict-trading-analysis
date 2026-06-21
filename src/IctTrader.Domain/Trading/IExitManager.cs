using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// Runs the §2.5.9 exit machinery for one candle on one open <see cref="PaperTrade"/> (plan §3.4). It composes the
/// protective fill, the T1 scale-out, and the stop trail into one ordered <see cref="ExitPlan"/> the caller applies —
/// the DECIDE half, consistent with <see cref="IFillEvaluator"/> / <see cref="IStopTrailPolicy"/>. PURE: it injects
/// the sub-decision services and reads the bar-close time from the <see cref="ExitContext"/>, never an ambient clock.
/// The max-hold / no-overnight time-exit is a deferred follow-on cut.
/// </summary>
public interface IExitManager
{
    ExitPlan Decide(PaperTrade trade, Candle candle, ExitContext context);
}
