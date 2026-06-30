using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Confluence;

/// <summary>
/// The rankable facets of one confirmed advisory setup, in DOMAIN types — so the
/// <see cref="SignalRanker"/> stays pure (it never references a module's wire DTO). The Scanning module projects each
/// <c>SetupDto</c> to this (parsing the wire enum names) before ranking, and carries an opaque
/// <typeparamref name="TPayload"/> (the original DTO) through so the ranked result can be re-projected to the wire.
/// </summary>
/// <param name="Grade">The confluence grade (A &gt; B &gt; C &gt; Reject) — the PRIMARY sort key.</param>
/// <param name="Score">The 0–100 confluence score (§2.5.4) — the secondary key within a grade.</param>
/// <param name="RewardRatio">The plan reward-to-risk — the tertiary key (a bigger edge ranks higher).</param>
/// <param name="Timeframe">The trigger timeframe — the §4.7 conviction tie-break (via the options' priority map).</param>
/// <param name="DetectedAtUtc">The detection time — the final tie-break (newer ranks first).</param>
/// <param name="Payload">The opaque carried value (the wire DTO) re-emitted in rank order.</param>
public readonly record struct RankableSignal<TPayload>(
    SetupGrade Grade,
    int Score,
    decimal RewardRatio,
    Timeframe Timeframe,
    DateTimeOffset DetectedAtUtc,
    TPayload Payload);

/// <summary>
/// The pure domain service that orders confirmed advisory setups into the "best opportunities" ranking across the
/// whole (symbol × timeframe × style) matrix — "the system suggests the best setup". It is a deterministic,
/// total-order comparer (a tie in every key falls back to detection time then a stable payload-independent order), so
/// the same set always ranks identically. All weights/priorities come from <see cref="SignalRankingOptions"/> — no
/// magic numbers.
///
/// <para><b>Ordering (each key descending unless noted):</b>
/// <list type="number">
///   <item><description>Grade — A &gt; B &gt; C &gt; Reject (the §2.5.4 conviction tier).</description></item>
///   <item><description>Score — the 0–100 confluence score within the grade.</description></item>
///   <item><description>RewardRatio — the bigger planned edge.</description></item>
///   <item><description>Timeframe priority — the §4.7 higher-conviction frame (from the options' map).</description></item>
///   <item><description>Recency — the newer setup (a fleeting intraday opportunity outranks an older one).</description></item>
/// </list></para>
///
/// <para><b>Advisory-only (plan §6.3):</b> ranking advisory setups produces an advisory ordering; it routes nowhere
/// near an order path.</para>
/// </summary>
public sealed class SignalRanker
{
    private readonly SignalRankingOptions _options;

    public SignalRanker(SignalRankingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Returns <paramref name="signals"/> ordered best-first by the documented total order. Pure — it copies the input
    /// to a new ordered list and never mutates the source. A null/empty input yields an empty list.
    /// </summary>
    public IReadOnlyList<RankableSignal<TPayload>> Rank<TPayload>(IEnumerable<RankableSignal<TPayload>> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        // OrderBy chains are stable in .NET, but the recency key already makes the order TOTAL for any two distinct
        // setups (a symbol confirms one setup per bar-close, so two distinct setups differ in at least grade/score/
        // RR/TF/time) — so the result is deterministic regardless of input order.
        return signals
            .OrderByDescending(s => s.Grade)                      // A > B > C > Reject
            .ThenByDescending(s => s.Score)                       // higher confluence score
            .ThenByDescending(s => s.RewardRatio)                 // bigger planned edge
            .ThenByDescending(s => _options.PriorityFor(s.Timeframe)) // §4.7 higher-conviction frame
            .ThenByDescending(s => s.DetectedAtUtc)               // newer first
            .ToList();
    }
}
