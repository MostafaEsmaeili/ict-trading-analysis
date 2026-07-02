using IctTrader.Domain.Detection;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Setups;

/// <summary>
/// The per-cell confluence state machine a <see cref="ScanSession"/> drives: it folds each candle's detector
/// matches into an in-flight candidate and yields a graded <see cref="SetupConfirmation"/> when one confirms.
/// <see cref="SetupCandidate"/> (the §2.5 FSM — MSS direction lock, sweep-before-MSS, standing-vs-latched
/// conditions) is the canonical implementation; the seam exists so a future setup model whose confirmation
/// semantics genuinely cannot be expressed by parameterizing <see cref="SetupCandidateOptions"/> can supply its
/// own FSM WITHOUT touching the session/scanner plumbing. Do not add a second implementation speculatively —
/// parameterize the canonical FSM first (plan §16 D2).
/// </summary>
public interface ISetupCandidate
{
    /// <summary>The locked trade direction, or null while no shift has set one.</summary>
    Direction? LockedDirection { get; }

    /// <summary>Whether the candidate is holding any accumulated state (for the session's reset bookkeeping).</summary>
    bool HasActivity { get; }

    /// <summary>
    /// Folds one candle's confluence matches into the candidate and returns a <see cref="SetupConfirmation"/>
    /// when the setup confirms at or above the alert floor (resetting itself so it is not re-emitted), else null.
    /// </summary>
    SetupConfirmation? Observe(MarketContext context, Candle current, IReadOnlyList<ConfluenceMatch> matches);

    /// <summary>Clears all accumulated state (day/killzone boundary or anchor invalidation teardown).</summary>
    void Reset();
}
