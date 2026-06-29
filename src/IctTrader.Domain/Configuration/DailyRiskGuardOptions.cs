namespace IctTrader.Domain.Configuration;

/// <summary>
/// The §2.4/§2.5.5 "circuit-breaker" risk discipline (bound from <c>Ict:Risk:DailyGuard</c>, nested under the same
/// <c>Ict:Risk</c> subtree as <see cref="RiskOptions"/>) — no magic numbers. Where the adaptive
/// <see cref="IctTrader.Domain.Trading.IRiskManager"/> sizes the next trade DOWN as losses accrue (the loss-ladder),
/// this HALTS new entries for the rest of the NY trading day once the day turns sour, encoding ICT's "stop after a run
/// of losses, cap the daily bleed, walk away — don't push buttons" discipline (Ep41 revenge/loser's-cycle lecture;
/// Ep37 "stop trading … don't try to push any buttons"; Ep18 "walk away / pull the order"). The two share one
/// discipline: the ladder shrinks size while a drawdown is recoverable, the guard stops trading once it is not.
/// <para>
/// <b>Provenance.</b> The DISCIPLINE is transcript-sourced; the NUMERIC thresholds are NOT 2022-Mentorship-verbatim and
/// are provenance-flagged (community/prop-firm canon, consistent with the Ep41 spirit). The transcript-honest defaults
/// here bracket the disciplined ladder: <see cref="ConsecutiveLossHaltThreshold"/>=3 (you have taken all three
/// 1%→0.5%→0.25% ladder losses — "0.25% is the lowest you can go", you are out of size), and
/// <see cref="DailyLossCapPercent"/>=2.0 (the disciplined 1+0.5+0.25 ladder loses ≈1.75%; 2.0 is the round community
/// cap, Ep41 warns the martingale path "very easily" reaches 10%). Both stay configurable per operator.
/// </para>
/// <para>
/// This is a NON-scoring account-discipline gate, NOT a <see cref="IctTrader.Domain.Detection.ConfluenceCondition"/> —
/// it never changes a setup's grade (a Grade-A setup is still Grade A during a halt; we simply decline to ACT on it),
/// so the Σ=9.75 weighted universe is untouched. It only WITHHOLDS paper entries — it routes nothing (guardrail-clean
/// by shape, like the calendar gate). Default <see cref="Enabled"/>=false keeps existing backtests/tests byte-identical;
/// it is recommended-on in the operator-facing live/optimized configs.
/// </para>
/// </summary>
public sealed class DailyRiskGuardOptions
{
    public const string SectionName = "Ict:Risk:DailyGuard";

    /// <summary>The §2.5.5 methodology ceiling on a daily loss cap — a sane upper bound so a misconfigured cap can't
    /// swallow the whole account; mirrors the <see cref="RiskOptions"/> hard-max pattern. Not a magic number: a daily
    /// loss beyond this is never disciplined ICT risk.</summary>
    private const decimal AbsoluteMaxDailyLossCapPercent = 10.0m;

    /// <summary>When false (the config default) the guard never halts — entries are always admitted, so existing
    /// backtests/E2E stay byte-identical. Recommended-on in the live/optimized configs (canonical ICT discipline, not a
    /// §2.5 deviation).</summary>
    public bool Enabled { get; init; }

    /// <summary>Halt NEW entries once this many consecutive losses (gross structural outcome) have accrued. Default 3 =
    /// the three-rung 1%→0.5%→0.25% ladder is exhausted (Ep41). INVENTED/community-flagged — N is not verbatim in the
    /// 2022 Mentorship (2 is the conservative community alternative).</summary>
    public int ConsecutiveLossHaltThreshold { get; init; } = 3;

    /// <summary>Halt NEW entries once the day's cumulative REALIZED net loss reaches this percent of account equity.
    /// Default 2.0 (the round community cap; the disciplined ladder loses ≈1.75%, the disaster path reaches ~10%).
    /// INVENTED/spirit-derived — no hard daily % is stated in the 2022 Mentorship.</summary>
    public decimal DailyLossCapPercent { get; init; } = 2.0m;

    /// <summary>True (default) resets the daily-loss tally and the daily-halt latch at the 00:00-NY day rollover (the same
    /// §2.1 boundary the no-overnight time-exit uses). The reset itself is performed by the caller that owns the per-day
    /// tally; this flag documents and gates that policy. The account-level consecutive-loss STREAK is unaffected — it is
    /// owned by <see cref="IctTrader.Domain.Trading.PaperAccount"/> for sizing and persists across days.</summary>
    public bool ResetAtNyDayRollover { get; init; } = true;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (ConsecutiveLossHaltThreshold < 1)
        {
            errors.Add(
                $"ConsecutiveLossHaltThreshold must be at least 1 but was {ConsecutiveLossHaltThreshold}.");
        }

        if (DailyLossCapPercent <= 0m || DailyLossCapPercent > AbsoluteMaxDailyLossCapPercent)
        {
            errors.Add(
                $"DailyLossCapPercent must be within (0, {AbsoluteMaxDailyLossCapPercent}] but was {DailyLossCapPercent}.");
        }

        return errors;
    }
}
