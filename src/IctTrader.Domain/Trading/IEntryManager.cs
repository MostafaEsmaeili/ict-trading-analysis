using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// Drives one resting <see cref="ArmedEntry"/> for one candle (plan §2.5.1 step 7): it first runs the no-chase
/// cancellation precedence (killzone-end &gt; max-wait — an unfilled limit whose entry window has passed is cancelled,
/// never chased), then decides whether the §2.5.1 limit fills and, if a fast bar fills it AND runs to the stop the same
/// bar, resolves that entry-then-stop straddle to a worst-case −1R — never a phantom favorable outcome. The DECIDE
/// half, mirroring <see cref="IExitManager"/>; pure and clock-free (the only "now" is the bar-close time on the
/// <see cref="EntryContext"/>).
/// </summary>
public interface IEntryManager
{
    EntryPlan Decide(ArmedEntry armedEntry, Candle candle, EntryContext context);
}
