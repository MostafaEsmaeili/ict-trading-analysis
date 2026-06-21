using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// Builds one sized <see cref="PaperTrade"/> from a confirmed advisory <see cref="Setup"/> and a
/// <see cref="PaperAccount"/> (plan §3.0 factory / §5.1). It sizes the position from the configured base risk,
/// confirms the account can reserve that risk within the portfolio cap, opens the trade at the plan's prices,
/// and reserves the risk on the account — a single atomic step. The trade inherits the Setup's bias-aligned
/// direction, so a counter-bias trade is structurally impossible (a counter-bias setup never becomes a Setup).
/// This slice uses the flat <see cref="RiskOptions.BaseRiskPercent"/>; the adaptive loss-ladder is a fast-follow.
/// </summary>
public sealed class PaperTradeFactory
{
    private readonly RiskOptions _risk;

    public PaperTradeFactory(RiskOptions risk)
    {
        ArgumentNullException.ThrowIfNull(risk);
        _risk = risk;
    }

    public PaperTrade Open(
        Setup setup,
        PaperAccount account,
        SymbolSpec symbolSpec,
        ContractSpec contractSpec,
        DateTimeOffset openedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(symbolSpec);
        ArgumentNullException.ThrowIfNull(contractSpec);

        var sizing = PositionSizer.Size(
            account.Equity,
            new RiskPercent(_risk.BaseRiskPercent),
            setup.Plan,
            symbolSpec,
            contractSpec,
            new Pips(_risk.MinStopDistancePips));

        var trade = new PaperTrade(
            Guid.NewGuid(),
            account.Id,
            setup.Symbol,
            setup.Style,
            setup.Timeframe,
            setup.Plan,
            sizing.Size,
            symbolSpec.PipSize,
            contractSpec.ValuePerPip,
            openedAtUtc);

        // The account is the authoritative cap gate: it throws (without mutating) if the trade would breach the
        // portfolio open-risk cap, so the open is atomic — a refused trade leaves the account untouched.
        account.RegisterOpen(trade);
        return trade;
    }

    /// <summary>
    /// ARMS a confirmed advisory <see cref="Setup"/> as a resting limit at the §2.5.1-step-7 entry: it sizes the
    /// position ONCE (at arm-time equity) and reserves that exact risk against the account's portfolio cap, returning
    /// the resting <see cref="ArmedEntry"/> whose id becomes the future trade id. Atomic: the account throws (without
    /// mutating) if the resting limit would breach the cap, so a refused arm leaves the account untouched and no
    /// <see cref="ArmedEntry"/> escapes. Because the size is frozen here, the trade opened later by
    /// <see cref="OpenArmed"/> derives a <see cref="PaperTrade.RiskBudget"/> exactly equal to the reserved budget.
    /// </summary>
    public ArmedEntry Arm(
        Setup setup,
        PaperAccount account,
        SymbolSpec symbolSpec,
        ContractSpec contractSpec,
        DateTimeOffset armedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(symbolSpec);
        ArgumentNullException.ThrowIfNull(contractSpec);

        var sizing = PositionSizer.Size(
            account.Equity,
            new RiskPercent(_risk.BaseRiskPercent),
            setup.Plan,
            symbolSpec,
            contractSpec,
            new Pips(_risk.MinStopDistancePips));

        // Construct the ArmedEntry BEFORE reserving so the arm is atomic (mirroring how Open builds the trade before
        // RegisterOpen): a bad entity ctor (e.g. a non-UTC time) throws before any reservation, and a cap-refused
        // Reserve discards the un-returned ArmedEntry — either path leaves the account untouched.
        var armedEntry = new ArmedEntry(
            Guid.NewGuid(), account.Id, setup, sizing.Size, sizing.RiskBudget, armedAtUtc);
        account.Reserve(armedEntry.Id, sizing.RiskBudget);

        return armedEntry;
    }

    /// <summary>
    /// TRIGGERS a resting <see cref="ArmedEntry"/> into an open <see cref="PaperTrade"/> when the entry touch fills
    /// (plan §2.5.1 step 7): the trade opens under the SAME id as the armed entry, sized with the SAME arm-time
    /// position, stamped at the fill bar-close time. It deliberately does NOT call <see cref="PaperAccount.RegisterOpen"/>
    /// — the risk is ALREADY reserved under this id (at arm time), so the trigger is a key RE-LABEL, not a second
    /// reservation; calling RegisterOpen would throw (already reserved) and re-check the cap against a ledger that
    /// already counts this exposure. The account's existing <see cref="PaperAccount.Settle"/> finds the trade's id in
    /// the ledger and releases it exactly as for an immediately-opened trade — so the open-trade bookkeeping is
    /// byte-unchanged. The same-bar entry-then-stop straddle is resolved by the orchestrator (cut 2b), not here.
    /// </summary>
    public PaperTrade OpenArmed(
        ArmedEntry armedEntry,
        SymbolSpec symbolSpec,
        ContractSpec contractSpec,
        DateTimeOffset openedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(armedEntry);
        ArgumentNullException.ThrowIfNull(symbolSpec);
        ArgumentNullException.ThrowIfNull(contractSpec);

        // Fail-fast: reject a second trigger BEFORE constructing a PaperTrade (which would raise a discarded
        // PaperTradeOpened event). MarkTriggered guards the once-only Armed→Triggered transition and stamps
        // EntryTriggered at the fill bar-close time.
        armedEntry.MarkTriggered(openedAtUtc);

        var setup = armedEntry.Setup;
        return new PaperTrade(
            armedEntry.Id, // the trade id IS the reservation id — the trigger is a key re-label, not a re-reserve
            armedEntry.AccountId,
            setup.Symbol,
            setup.Style,
            setup.Timeframe,
            setup.Plan,
            armedEntry.Size, // the frozen arm-time size, so the derived RiskBudget == the reserved budget (no drift)
            symbolSpec.PipSize,
            contractSpec.ValuePerPip,
            openedAtUtc);
    }
}
