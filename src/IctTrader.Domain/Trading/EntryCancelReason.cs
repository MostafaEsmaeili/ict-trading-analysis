namespace IctTrader.Domain.Trading;

/// <summary>
/// Why a resting <see cref="ArmedEntry"/> was cancelled before it filled (plan §2.5.1 "we don't chase it"). The
/// orchestrator's no-chase precedence decides which fires: <see cref="KillzoneEnded"/> (the bar left the active entry
/// window — killzone over, lunch, or the index cutoff) takes precedence over the <see cref="MaxWaitElapsed"/> backstop.
/// The no-overnight boundary is NOT a separate reason here: for the FX killzone schedule no active killzone spans
/// 00:00 NY, so any midnight cross already trips <see cref="KillzoneEnded"/> (unlike the exit time-exit, where
/// no-overnight is load-bearing for a HELD trade). Structural setup-invalidation (needs market structure) and
/// bar-count ageout (needs a bar counter) are deferred.
/// </summary>
public enum EntryCancelReason
{
    /// <summary>The bar is no longer a tradeable killzone entry — the §2.5.1-step-3 entry window passed.</summary>
    KillzoneEnded,

    /// <summary>The limit rested longer than the operator's max-wait cap (INVENTED — no transcript max-WAIT).</summary>
    MaxWaitElapsed,
}
