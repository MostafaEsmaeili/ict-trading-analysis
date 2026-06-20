using IctTrader.Domain.Common;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// A simulated, ADVISORY-sourced trade (plan §3.0/§5.1/§5.2) — the aggregate root that expresses one position's
/// lifecycle independent of persistence and transport. Construction OPENS the trade at the plan's entry (the
/// §5.1 immediate-open path; the realistic entry-touch fill is WP5). It freezes <see cref="InitialRiskPerUnit"/>
/// at open so realized R is always measured against the original 1R even after a later stop move (§5.2), and it
/// derives its own <see cref="RiskBudget"/> from the same money geometry it books P&amp;L with, so the risk it
/// reserves on the account and its stop-out loss can never disagree. <see cref="Close"/> realizes the gross R
/// and P&amp;L; costs (§5.4), partial scale-outs, breakeven arming and time-exit are WP5. Paper only: the trade
/// writes to its own state, never routes an order (§6.3).
/// </summary>
public sealed class PaperTrade : AggregateRoot<Guid>
{
    private readonly decimal _pipSize;
    private readonly decimal _valuePerPipForPosition;

    public PaperTrade(
        Guid id,
        Guid accountId,
        Symbol symbol,
        TradeStyle style,
        Timeframe timeframe,
        TradePlan plan,
        PositionSize size,
        decimal pipSize,
        decimal valuePerPip,
        DateTimeOffset openedAtUtc)
        : base(id)
    {
        Guard.Against(id == Guid.Empty, "PaperTrade requires a non-empty id.");
        Guard.Against(accountId == Guid.Empty, "PaperTrade requires the owning account id.");
        Guard.Against(symbol is null, "PaperTrade requires a symbol.");
        Guard.Against(pipSize <= 0m, "PaperTrade requires a positive pip size.");
        Guard.Against(valuePerPip <= 0m, "PaperTrade requires a positive value-per-pip.");
        Guard.Against(openedAtUtc.Offset != TimeSpan.Zero, "PaperTrade.OpenedAtUtc must be UTC.");

        var initialRiskPerUnit = Math.Abs(plan.Entry.Value - plan.Stop.Value);
        Guard.Against(initialRiskPerUnit <= 0m, "PaperTrade stop must differ from entry.");

        AccountId = accountId;
        Symbol = symbol!;
        Style = style;
        Timeframe = timeframe;
        Plan = plan;
        Size = size;
        OpenedAtUtc = openedAtUtc;
        InitialRiskPerUnit = initialRiskPerUnit;
        Status = TradeStatus.Open;

        _pipSize = pipSize;
        _valuePerPipForPosition = valuePerPip * size.Lots;

        // The money at risk if the stop is hit, derived from the SAME geometry that books P&L, so the reserved
        // risk and a realized stop-out loss are guaranteed equal.
        RiskBudget = new Money(initialRiskPerUnit / pipSize * _valuePerPipForPosition);

        RaiseDomainEvent(new PaperTradeOpened(Id, AccountId, Symbol, Direction, Size, openedAtUtc));
    }

    public Guid AccountId { get; }

    public Symbol Symbol { get; }

    public TradeStyle Style { get; }

    public Timeframe Timeframe { get; }

    public TradePlan Plan { get; }

    public PositionSize Size { get; }

    /// <summary>The money at risk if the stop is hit — the trade's reserved share of the portfolio cap.</summary>
    public Money RiskBudget { get; }

    public DateTimeOffset OpenedAtUtc { get; }

    /// <summary>The original |entry − stop| in price units, frozen at open so R is always vs the original 1R.</summary>
    public decimal InitialRiskPerUnit { get; }

    public TradeStatus Status { get; private set; }

    public Price? ExitPrice { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public TradeCloseReason? CloseReason { get; private set; }

    /// <summary>The signed realized reward-to-risk in R (−1 at a full stop-out), set on close.</summary>
    public decimal? RealizedR { get; private set; }

    /// <summary>The signed gross realized P&amp;L in account currency, set on close (net costs are WP5).</summary>
    public Money? RealizedPnl { get; private set; }

    public Direction Direction => Plan.Direction;

    public Price Entry => Plan.Entry;

    public Price Stop => Plan.Stop;

    /// <summary>
    /// Closes the trade at <paramref name="exitPrice"/>, realizing the gross R and P&amp;L. Legal only from
    /// <see cref="TradeStatus.Open"/>. A close at exactly the stop yields −1R and a loss equal to the risk budget;
    /// a close at the runner target yields the plan's reward-to-risk.
    /// </summary>
    public void Close(Price exitPrice, TradeCloseReason reason, DateTimeOffset closedAtUtc)
    {
        Guard.Against(Status != TradeStatus.Open, "Only an open paper trade can be closed.");
        Guard.Against(closedAtUtc.Offset != TimeSpan.Zero, "PaperTrade.ClosedAtUtc must be UTC.");
        Guard.Against(closedAtUtc < OpenedAtUtc, "A paper trade cannot close before it opened.");

        var signedMove = Direction == Direction.Bullish
            ? exitPrice.Value - Entry.Value
            : Entry.Value - exitPrice.Value;

        var realizedR = signedMove / InitialRiskPerUnit;
        var realizedPnl = new Money(signedMove / _pipSize * _valuePerPipForPosition);

        ExitPrice = exitPrice;
        CloseReason = reason;
        ClosedAtUtc = closedAtUtc;
        RealizedR = realizedR;
        RealizedPnl = realizedPnl;
        Status = TradeStatus.Closed;

        RaiseDomainEvent(new PaperTradeClosed(Id, AccountId, realizedR, realizedPnl, reason, closedAtUtc));
    }
}
