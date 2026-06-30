using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>Why the daily risk guard halted new entries (advisory, surfaced on the suppression reason).</summary>
public enum DailyRiskHaltReason
{
    /// <summary>Not halted — entries are admitted.</summary>
    None,

    /// <summary>The account's consecutive-loss streak reached <see cref="DailyRiskGuardOptions.ConsecutiveLossHaltThreshold"/>.</summary>
    ConsecutiveLosses,

    /// <summary>The day's cumulative realized net loss reached <see cref="DailyRiskGuardOptions.DailyLossCapPercent"/> of equity.</summary>
    DailyLossCap,
}

/// <summary>The pure DECIDE result of <see cref="IDailyRiskGuard.Evaluate"/>: may new entries be admitted, and if not, why.</summary>
public readonly record struct DailyRiskGuardDecision(bool EntriesAllowed, DailyRiskHaltReason Reason)
{
    public static DailyRiskGuardDecision Allowed { get; } = new(true, DailyRiskHaltReason.None);

    public static DailyRiskGuardDecision Halt(DailyRiskHaltReason reason) => new(false, reason);
}

/// <summary>The §2.4/§2.5.5 circuit-breaker (plan §2.5.5; Ep41/Ep37/Ep18 discipline) — see <see cref="DailyRiskGuard"/>.</summary>
public interface IDailyRiskGuard
{
    /// <summary>Decides whether a NEW entry may be admitted given the account's streak/equity state and the day's
    /// realized P&amp;L so far. Pure and clock-free — the day's tally + NY-day reset are owned by the caller.</summary>
    DailyRiskGuardDecision Evaluate(RiskState state, Money dayRealizedPnl, DailyRiskGuardOptions options);
}

/// <summary>
/// The §2.4/§2.5.5 "circuit-breaker" risk discipline (Ep41 revenge/loser's-cycle; Ep37 "stop pushing buttons"; Ep18
/// "walk away"). It HALTS new paper entries for the rest of the NY trading day once the day turns sour — the missing
/// enforcement half of a model the engine already half-implements (the <see cref="IRiskManager"/> ladder sizes DOWN but
/// never STOPS). It composes with the ladder: while a drawdown is recoverable and below the halt threshold you still
/// trade at the laddered-down size; once the guard trips you stop entering entirely for the day.
/// <para>
/// Pure / DECIDE-only (mirrors <see cref="FillEvaluator"/>/<see cref="StopTrailPolicy"/>): it touches no clock and no
/// I/O — the per-NY-day realized-loss tally and its 00:00-NY reset are computed by the caller and passed in; the
/// orchestrator APPLIES the decision (declines to arm/open). It only WITHHOLDS entries, never routes an order (§6.3).
/// A "loss" is the GROSS structural outcome (mirroring <see cref="PaperAccount"/>: a cost-only scratch is a breakeven,
/// not a loss) for the consecutive-loss streak; the daily cap is measured on NET realized P&amp;L (real-money drawdown).
/// </para>
/// </summary>
public sealed class DailyRiskGuard : IDailyRiskGuard
{
    private const decimal PercentDivisor = 100m;

    public DailyRiskGuardDecision Evaluate(RiskState state, Money dayRealizedPnl, DailyRiskGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return DailyRiskGuardDecision.Allowed;
        }

        // Rung 1: a run of consecutive losses (the account streak that also drives the loss-ladder) — "stop after the
        // ladder is exhausted" — but ONLY once the DAY is also in the red, so this is a per-NY-DAY circuit breaker that
        // RESETS each day (the caller zeroes the day tally at 00:00 NY). The day-down gate is load-bearing: the streak
        // (state.ConsecutiveLosses) is LIFETIME and only clears on a WIN, so halting on it UNCONDITIONALLY deadlocks the
        // strategy after the threshold is hit (no entry admitted → no win possible → halted forever). Gating on a red day
        // means a fresh day (tally 0) always admits the first entry — a chance to win and clear the streak — while a day
        // that has already bled keeps the loser's-cycle shut (Ep41/Ep37/Ep18 "walk away for the day").
        if (state.ConsecutiveLosses >= options.ConsecutiveLossHaltThreshold && dayRealizedPnl.Amount < 0m)
        {
            return DailyRiskGuardDecision.Halt(DailyRiskHaltReason.ConsecutiveLosses);
        }

        // Rung 2: the day's realized bleed reached the cap. Only a net LOSS counts (a green day never halts even after an
        // intraday dip); the cap is a fraction of current equity. dayRealizedPnl is signed (positive = net up on the day).
        var dayLossMagnitude = Math.Max(0m, -dayRealizedPnl.Amount);
        var capAmount = options.DailyLossCapPercent / PercentDivisor * state.CurrentEquity.Amount;
        if (capAmount > 0m && dayLossMagnitude >= capAmount)
        {
            return DailyRiskGuardDecision.Halt(DailyRiskHaltReason.DailyLossCap);
        }

        return DailyRiskGuardDecision.Allowed;
    }
}
