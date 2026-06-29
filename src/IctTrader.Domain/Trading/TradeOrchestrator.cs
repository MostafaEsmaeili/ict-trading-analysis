using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The per-candle paper-trade lifecycle process (plan §3.4) — a pure domain composer (no I/O, no ambient clock; the only
/// "now" is the caller-passed bar-close time) that wires the entry and exit DECIDE halves into one runnable cycle and
/// APPLIES their plans to the aggregates. It is the FIRST consumer of <see cref="EntryManager"/> + <see cref="ExitManager"/>
/// + <see cref="PaperTradeFactory"/> together, so a confirmed advisory <see cref="Setup"/> can be carried end-to-end:
/// arm → fill → manage → close, with the portfolio cap kept consistent at every step.
/// <para>
/// <b>Per-candle precedence.</b> A resting <see cref="ArmedEntry"/> runs the entry pass first: the
/// <see cref="EntryManager"/> may cancel it (no-chase — apply <see cref="ArmedEntry.Cancel"/> + <see cref="PaperAccount.Release"/>
/// so the cap self-heals), or open it (<see cref="PaperTradeFactory.OpenArmed"/>), or open-then-close it the same bar
/// (the −1R straddle — open, then <see cref="PaperTrade.Close"/> + settle). After a CLEAN open (no same-bar straddle),
/// the SAME candle is re-fed to the exit pass, so a same-bar runner / T1 / trail that genuinely traded is booked on the
/// entry bar rather than missed — the entry path itself only books the protective straddle, by design. An already-open
/// trade runs the exit pass directly. Every terminal close settles immediately, releasing the trade's reserved risk so
/// the ~5% cap (§2.5.10) is never transiently over-counted.
/// </para>
/// Paper only — every action it applies writes to our own aggregates/account; it routes nothing (§6.3).
/// </summary>
public sealed class TradeOrchestrator : ITradeOrchestrator
{
    private readonly IEntryManager _entryManager;
    private readonly IExitManager _exitManager;
    private readonly PaperTradeFactory _factory;
    private readonly EntryManagementOptions _entryOptions;
    private readonly IDailyRiskGuard? _dailyRiskGuard;
    private readonly DailyRiskGuardOptions? _dailyRiskGuardOptions;

    public TradeOrchestrator(
        IEntryManager entryManager,
        IExitManager exitManager,
        PaperTradeFactory factory,
        EntryManagementOptions entryOptions,
        IDailyRiskGuard? dailyRiskGuard = null,
        DailyRiskGuardOptions? dailyRiskGuardOptions = null)
    {
        ArgumentNullException.ThrowIfNull(entryManager);
        ArgumentNullException.ThrowIfNull(exitManager);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(entryOptions);
        _entryManager = entryManager;
        _exitManager = exitManager;
        _factory = factory;
        _entryOptions = entryOptions;
        // The §2.4/§2.5.5 circuit-breaker is OPTIONAL: when unwired (or its options omitted) the guard is never
        // consulted, so the orchestrator behaves exactly as before — every existing call site stays byte-identical.
        _dailyRiskGuard = dailyRiskGuard;
        _dailyRiskGuardOptions = dailyRiskGuardOptions;
    }

    public ManagedPosition OnSetupConfirmed(
        Setup setup, PaperAccount account, SymbolSpec symbolSpec, ContractSpec contractSpec, DateTimeOffset atUtc,
        Guid setupId = default, Money? dayRealizedPnl = null)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(symbolSpec);
        ArgumentNullException.ThrowIfNull(contractSpec);
        Guard.Against(atUtc.Offset != TimeSpan.Zero, "TradeOrchestrator.OnSetupConfirmed time must be UTC.");

        // §2.4/§2.5.5 daily risk guard (Ep41/Ep37/Ep18 discipline): once the day is halted (a run of losses or the
        // realized daily-loss cap), decline NEW entries for the rest of the NY day — the setup stays a graded advisory
        // we simply don't act on, so NO ArmedEntry/PaperTrade is created and NO risk is reserved against the cap. The
        // caller owns the per-NY-day realized-P&L tally (and its 00:00-NY reset); a null tally skips the guard.
        if (_dailyRiskGuard is not null && _dailyRiskGuardOptions is not null && dayRealizedPnl.HasValue
            && !_dailyRiskGuard.Evaluate(account.RiskState, dayRealizedPnl.Value, _dailyRiskGuardOptions).EntriesAllowed)
        {
            return ManagedPosition.None;
        }

        // EntryMode.Immediate opens at the plan entry now (§5.1); the default Armed rests a limit at the OTE/FVG level
        // and reserves its risk, waiting for the retrace (§2.5.1 step 7). Both reserve against the same portfolio cap.
        // The deterministic setup id is threaded into the opened/armed aggregate so the seam is idempotent (a
        // redelivered/restart-re-streamed setup re-derives the SAME aggregate id — the handler short-circuits on it).
        return _entryOptions.Mode == EntryMode.Immediate
            ? ManagedPosition.Live(_factory.Open(setup, account, symbolSpec, contractSpec, atUtc, setupId))
            : ManagedPosition.Resting(_factory.Arm(setup, account, symbolSpec, contractSpec, atUtc, setupId));
    }

    public ManagedPosition Advance(ManagedPosition position, PaperAccount account, Candle candle, DateTimeOffset barCloseUtc)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(account);

        if (position.HasRestingEntry)
        {
            ApplyEntry(position, account, candle, barCloseUtc);

            // Same-bar re-feed: after a CLEAN open (the trade is still open — no protective same-bar straddle closed it),
            // run the exit pass on THIS bar so a same-bar runner / T1 / trail that genuinely traded is booked on the
            // entry bar, not deferred to the next. A straddle-closed trade is already closed, so this is skipped.
            if (position.HasOpenTrade)
            {
                ApplyExit(position, account, candle, barCloseUtc);
            }

            return position;
        }

        if (position.HasOpenTrade)
        {
            ApplyExit(position, account, candle, barCloseUtc);
        }

        return position;
    }

    private void ApplyEntry(ManagedPosition position, PaperAccount account, Candle candle, DateTimeOffset barCloseUtc)
    {
        var armed = position.Armed!;
        var plan = _entryManager.Decide(armed, candle, new EntryContext(barCloseUtc));

        // The plan is apply-ordered: a lone Open (clean fill), Open then Close (the same-bar −1R straddle), or a lone
        // Cancel (no-chase). The Close always follows its Open, so position.Trade is set by the time it is applied.
        foreach (var action in plan.Actions)
        {
            switch (action.Kind)
            {
                case EntryActionKind.Cancel:
                    armed.Cancel(action.CancelReason!.Value, action.AtUtc);
                    account.Release(armed.Id);
                    break;

                case EntryActionKind.Open:
                    // action.AtUtc is the trigger bar's CLOSE (the fill time the max-hold math measures from); the
                    // trigger bar's OPEN (candle.OpenTimeUtc) is carried as the management-eligibility edge so the
                    // per-candle handler first manages this trade on the bar after the trigger bar (M+1), not M+2.
                    position.AttachTrade(_factory.OpenArmed(armed, account, action.AtUtc, candle.OpenTimeUtc));
                    break;

                case EntryActionKind.Close:
                    Close(position, account, action.Price, action.Reason!.Value, action.Costs, action.AtUtc);
                    break;

                default:
                    throw new NotSupportedException($"Unhandled entry action kind '{action.Kind}'.");
            }
        }
    }

    private void ApplyExit(ManagedPosition position, PaperAccount account, Candle candle, DateTimeOffset barCloseUtc)
    {
        var trade = position.Trade!;
        var plan = _exitManager.Decide(trade, candle, new ExitContext(barCloseUtc));

        foreach (var action in plan.Actions)
        {
            switch (action.Kind)
            {
                case ExitActionKind.ScaleOut:
                    trade.ScaleOut(action.Price, action.LegSize!.Value, action.Costs, action.Reason!.Value, action.AtUtc);
                    break;

                case ExitActionKind.MoveStop:
                    trade.MoveStop(action.Price, action.AtUtc);
                    break;

                case ExitActionKind.Close:
                    Close(position, account, action.Price, action.Reason!.Value, action.Costs, action.AtUtc);
                    break;

                default:
                    throw new NotSupportedException($"Unhandled exit action kind '{action.Kind}'.");
            }
        }
    }

    /// <summary>Closes the position's trade and settles it on the account in the same step, so the trade's reserved risk
    /// is released back to the portfolio cap the moment it closes (no transient over-count).</summary>
    private static void Close(
        ManagedPosition position, PaperAccount account, Price level, TradeCloseReason reason, TradeCosts costs, DateTimeOffset atUtc)
    {
        var trade = position.Trade!;
        trade.Close(level, reason, costs, atUtc);
        account.Settle(trade);
    }
}
