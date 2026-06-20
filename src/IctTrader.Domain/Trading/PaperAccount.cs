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
    /// Books a closed trade: releases its reserved risk and applies its realized P&amp;L to equity. Throws if the
    /// trade is not this account's, is not closed, or was never reserved / already settled. Equity must stay
    /// positive — a single trade's risk is capped well below equity, so a normal stop-out cannot drain it.
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
