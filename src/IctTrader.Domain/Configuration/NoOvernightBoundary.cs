namespace IctTrader.Domain.Configuration;

/// <summary>
/// Which clock boundary a no-overnight trade style may not be held across (plan §2.5.1 step 9 "no overnight";
/// Ep21 "out before the close ... not holding 24 hours"). <see cref="NyMidnight"/> is the ICT financial-day start
/// (00:00 New York, §2.1 / Eps 2,10,18) and the ONLY boundary wired in this slice. <see cref="NyFxClose1700"/> (the
/// 17:00 ET FX rollover) is a forward-compat seam that couples to the deferred swap/rollover cost math — it is
/// defined but not yet honored: option validation rejects it at startup and the orchestrator throws if it is ever
/// reached, so an operator can never silently get unimplemented behavior.
/// </summary>
public enum NoOvernightBoundary
{
    /// <summary>00:00 New York — the ICT financial day boundary (default).</summary>
    NyMidnight,

    /// <summary>17:00 ET FX rollover — DEFERRED (not yet wired; rejected by validation).</summary>
    NyFxClose1700,
}
