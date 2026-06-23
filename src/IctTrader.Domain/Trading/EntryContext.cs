using IctTrader.Domain.Common;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The per-candle context the <see cref="IEntryManager"/> needs beyond the candle itself (plan §2.5.1 step 7). Like
/// <see cref="ExitContext"/> it carries the bar-CLOSE time (a <see cref="ValueObjects.Candle"/> exposes only its OPEN
/// time) so the orchestrator can stamp the decided open/close actions at the fill bar-close — the load-bearing
/// invariant that lets a same-bar open-then-close (the −1R straddle) pass the aggregate's monotonic timeline guard —
/// without ever reading an ambient clock. Self-validating: the time must be UTC.
/// </summary>
public readonly record struct EntryContext
{
    public EntryContext(DateTimeOffset barCloseUtc)
    {
        Guard.Against(barCloseUtc.Offset != TimeSpan.Zero, "EntryContext.BarCloseUtc must be UTC.");
        BarCloseUtc = barCloseUtc;
    }

    /// <summary>The UTC time the fill bar closed — the instant the decided open (and any same-bar straddle close) is stamped at.</summary>
    public DateTimeOffset BarCloseUtc { get; }
}
