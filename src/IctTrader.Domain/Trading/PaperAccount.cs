using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The simulated trading account (plan §3.0/§5.1) — the aggregate root that owns equity and the consistency
/// boundary for aggregate open risk. It is a ledger of its currently open trades (referenced by identity, not
/// by object): it admits a new trade only while the total reserved risk stays within the portfolio cap
/// (§2.5.10 ≈5%), and on settlement it releases exactly that trade's reserved risk and applies its realized
/// P&amp;L. Keying by trade id makes register/settle account-scoped and idempotent — a trade cannot be reserved
/// twice nor settled twice. Paper only: it writes to its own in-memory state, never to a broker (§6.3). The
/// adaptive loss-ladder / win-cycle that further throttles per-trade risk is a deferred fast-follow.
/// </summary>
public sealed class PaperAccount : AggregateRoot<Guid>
{
    private readonly Dictionary<Guid, Money> _reservedRiskByTrade = [];

    /// <summary>
    /// EF Core materialization constructor — private so domain consumers cannot call it and bypass the
    /// public constructor's invariant guards. EF sets <see cref="AggregateRoot{TId}.Id"/> and all mapped
    /// fields via backing-field access after construction (plan §7 persistence conventions). No domain
    /// invariants are weakened: this ctor is inaccessible outside the ORM and outside this assembly.
    /// </summary>
#pragma warning disable CS8618 // EF sets fields after construction via PropertyAccessMode.Field.
    private PaperAccount()
        : base(Guid.Empty)
    {
    }
#pragma warning restore CS8618

    public PaperAccount(Guid id, Money startingEquity, decimal maxOpenPortfolioRiskPercent)
        : base(id)
    {
        Guard.Against(id == Guid.Empty, "PaperAccount requires a non-empty id.");
        Guard.Against(!startingEquity.IsPositive, "PaperAccount must open with positive equity.");
        Guard.Against(
            maxOpenPortfolioRiskPercent is <= 0m or > 100m,
            $"MaxOpenPortfolioRiskPercent must be within (0, 100] but was {maxOpenPortfolioRiskPercent}.");

        Equity = startingEquity;
        MaxOpenPortfolioRiskPercent = maxOpenPortfolioRiskPercent;
    }

    public Money Equity { get; private set; }

    public decimal MaxOpenPortfolioRiskPercent { get; }

    /// <summary>The sum of the budgeted risk of every currently open trade.</summary>
    public Money OpenRisk => new(_reservedRiskByTrade.Values.Aggregate(0m, (sum, risk) => sum + risk.Amount));

    /// <summary>The most open risk allowed right now — <c>Equity × cap%</c>.</summary>
    public Money OpenRiskCap => new(Equity.Amount * MaxOpenPortfolioRiskPercent / 100m);

    /// <summary>True when reserving <paramref name="riskBudget"/> keeps total open risk within the cap.</summary>
    public bool CanOpen(Money riskBudget)
    {
        Guard.Against(!riskBudget.IsPositive, "A trade's risk budget must be positive.");
        return (OpenRisk + riskBudget) <= OpenRiskCap;
    }

    /// <summary>
    /// Reserves an open trade's risk against the portfolio cap. Throws if the trade is not this account's, is not
    /// open, is already reserved, or would breach the cap — so reservation is atomic and idempotent.
    /// </summary>
    public void RegisterOpen(PaperTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        Guard.Against(trade.AccountId != Id, "The trade belongs to a different account.");
        Guard.Against(trade.Status != TradeStatus.Open, "Only an open trade reserves risk.");
        Guard.Against(_reservedRiskByTrade.ContainsKey(trade.Id), "The trade is already reserved on this account.");
        Guard.Against(!CanOpen(trade.RiskBudget), "Opening this trade would exceed the portfolio open-risk cap.");

        _reservedRiskByTrade[trade.Id] = trade.RiskBudget;
    }

    /// <summary>
    /// Reserves a RESTING entry limit's risk against the portfolio cap BEFORE it becomes an open trade (plan §2.5.1
    /// step 7 / §2.5.10). It extends the SAME open-risk ledger that <see cref="RegisterOpen"/> uses, keyed by the
    /// armed entry's id (which becomes the trade id on trigger), so a resting limit competes for the SAME cap as an
    /// open trade — a same-bar burst of armed limits cannot momentarily breach it. Throws WITHOUT mutating if the id
    /// is empty, is already reserved, or the reservation would breach the cap, so arming is atomic. Equity is
    /// untouched: a reservation books no money — only a <see cref="Settle"/> does. On trigger the trade opens under
    /// this same id (via <see cref="PaperTradeFactory.OpenArmed"/>) so <see cref="Settle"/> releases it unchanged.
    /// </summary>
    public void Reserve(Guid reservationId, Money riskBudget)
    {
        Guard.Against(reservationId == Guid.Empty, "A reservation requires a non-empty id.");
        Guard.Against(
            _reservedRiskByTrade.ContainsKey(reservationId), "This id is already reserved on this account.");
        Guard.Against(!CanOpen(riskBudget), "Arming this entry would exceed the portfolio open-risk cap.");

        _reservedRiskByTrade[reservationId] = riskBudget;
    }

    /// <summary>
    /// Confirms a resting reservation exists on THIS account for the given id and budget — the gate
    /// <see cref="PaperTradeFactory.OpenArmed"/> checks before opening, so an armed trade can never open unbacked
    /// (bypassing the cap). Throws if the id was never reserved here (e.g. a hand-built <see cref="ArmedEntry"/> that
    /// did not go through <see cref="PaperTradeFactory.Arm"/>) or if the reserved risk does not match the entry's
    /// budget. The reservation itself stays keyed (the opened trade carries the same id); only <see cref="Settle"/>
    /// releases it.
    /// </summary>
    public void ConfirmArmedReservation(Guid reservationId, Money expectedBudget)
    {
        Guard.Against(
            !_reservedRiskByTrade.TryGetValue(reservationId, out var reserved),
            "No reservation exists on this account for this armed entry.");
        Guard.Against(
            reserved != expectedBudget, "The reserved risk does not match the armed entry's risk budget.");
    }

    /// <summary>
    /// Releases a resting reservation whose armed entry was cancelled unfilled (no-chase) — frees its risk back to the
    /// portfolio cap so a stale arm cannot leak it. Symmetric with <see cref="Reserve"/> (and distinct from
    /// <see cref="Settle"/>, which releases a reservation that BECAME a trade and books its P&amp;L). Throws if the id was
    /// never reserved here or was already released/settled; never touches <see cref="Equity"/> (a cancelled arm books
    /// no money).
    /// </summary>
    public void Release(Guid reservationId)
    {
        Guard.Against(
            !_reservedRiskByTrade.Remove(reservationId),
            "No reservation exists on this account to release.");
    }

    /// <summary>
    /// Books a closed trade: releases its reserved risk and applies its NET realized P&amp;L to equity (the §5.4
    /// costs are already netted into <see cref="PaperTrade.RealizedPnl"/>). The released risk is the original
    /// price-based <see cref="PaperTrade.RiskBudget"/>, unchanged by costs. Throws if the trade is not this
    /// account's, is not closed, or was never reserved / already settled. Equity must stay positive.
    /// </summary>
    public void Settle(PaperTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        Guard.Against(trade.AccountId != Id, "The trade belongs to a different account.");
        Guard.Against(trade.Status != TradeStatus.Closed, "Only a closed trade can be settled.");
        Guard.Against(
            !_reservedRiskByTrade.ContainsKey(trade.Id),
            "The trade is not open on this account (never reserved or already settled).");

        Guard.Against(trade.RealizedPnl is null, "A closed trade must carry a realized P&L to settle.");
        var newEquity = Equity + trade.RealizedPnl.Value;
        Guard.Against(!newEquity.IsPositive, "A settlement cannot drive account equity to zero or below.");

        _reservedRiskByTrade.Remove(trade.Id);
        Equity = newEquity;
    }
}
