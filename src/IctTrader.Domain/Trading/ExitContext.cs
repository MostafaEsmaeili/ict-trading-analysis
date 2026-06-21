using IctTrader.Domain.Common;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The per-candle context the <see cref="IExitManager"/> needs beyond the candle itself (plan §2.5.9). It carries
/// the bar-CLOSE time (a <see cref="ValueObjects.Candle"/> exposes only its OPEN time) so the orchestrator can stamp
/// the decided actions and the deferred time-exit (cut 2b) can measure max-hold / no-overnight without the aggregate
/// ever reading an ambient clock. Self-validating: the time must be UTC.
/// </summary>
public readonly record struct ExitContext
{
    public ExitContext(DateTimeOffset barCloseUtc)
    {
        Guard.Against(barCloseUtc.Offset != TimeSpan.Zero, "ExitContext.BarCloseUtc must be UTC.");
        BarCloseUtc = barCloseUtc;
    }

    /// <summary>The UTC time the bar closed — the instant the decided scale/trail/close actions are stamped at.</summary>
    public DateTimeOffset BarCloseUtc { get; }
}
