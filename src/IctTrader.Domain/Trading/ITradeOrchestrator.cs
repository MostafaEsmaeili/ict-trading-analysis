using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The per-candle paper-trade lifecycle process (plan §3.4) — the domain composer that finally wires the entry
/// (<see cref="IEntryManager"/>) and exit (<see cref="IExitManager"/>) DECIDE halves into a runnable cycle and APPLIES
/// their plans to the aggregates. <see cref="OnSetupConfirmed"/> turns a confirmed advisory <see cref="Setup"/> into a
/// <see cref="ManagedPosition"/> — a resting <see cref="ArmedEntry"/> (Armed mode) or an immediately opened
/// <see cref="PaperTrade"/> (Immediate mode) — and <see cref="Advance"/> moves that position forward exactly one candle:
/// it applies the entry plan (open / same-bar straddle / no-chase cancel), re-feeds the SAME bar to the exit pass after
/// a clean open so a same-bar runner/scale/trail is not missed, applies the exit plan (fill / time-exit / scale / trail),
/// and settles a terminally-closed trade promptly so the portfolio cap is never transiently over-counted. The domain
/// DECIDES and APPLIES here; the module handler only loads, persists, and publishes the collected domain events.
/// </summary>
public interface ITradeOrchestrator
{
    /// <summary>
    /// Turns a confirmed advisory setup into a managed position per the configured <c>EntryMode</c>. The optional
    /// <paramref name="setupId"/> (the deterministic <c>SetupDto.Id</c>) becomes the opened/armed aggregate id, so a
    /// redelivered/restart-re-streamed setup maps to the SAME id and the seam stays idempotent; default mints a fresh id.
    /// <para>
    /// When a <see cref="IDailyRiskGuard"/> is wired and enabled, <paramref name="dayRealizedPnl"/> (the account's net
    /// realized P&amp;L so far on the current NY trading day, computed by the caller who owns the per-day tally + its
    /// 00:00-NY reset) lets the §2.4/§2.5.5 circuit-breaker decline the setup: a halted day returns
    /// <see cref="ManagedPosition.None"/> — nothing armed/opened, no risk reserved. Passing <c>null</c> (the default)
    /// skips the guard entirely, so the unguarded path is byte-identical.
    /// </para>
    /// </summary>
    ManagedPosition OnSetupConfirmed(
        Setup setup, PaperAccount account, SymbolSpec symbolSpec, ContractSpec contractSpec, DateTimeOffset atUtc,
        Guid setupId = default, Money? dayRealizedPnl = null);

    /// <summary>Advances the position one candle, applying every decided entry/exit action and settling on close.</summary>
    ManagedPosition Advance(ManagedPosition position, PaperAccount account, Candle candle, DateTimeOffset barCloseUtc);
}
