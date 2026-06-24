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
    /// <summary>Turns a confirmed advisory setup into a managed position per the configured <c>EntryMode</c>.</summary>
    ManagedPosition OnSetupConfirmed(
        Setup setup, PaperAccount account, SymbolSpec symbolSpec, ContractSpec contractSpec, DateTimeOffset atUtc);

    /// <summary>Advances the position one candle, applying every decided entry/exit action and settling on close.</summary>
    ManagedPosition Advance(ManagedPosition position, PaperAccount account, Candle candle, DateTimeOffset barCloseUtc);
}
