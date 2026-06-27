using IctTrader.SharedKernel.Messaging;

namespace IctTrader.PaperTrading.Contracts;

// ---- DTOs (camelCase JSON; plan §11.1 #4) ----

/// <summary>
/// A simulated trade. There is no live counterpart anywhere in the system (plan §6.3). The leading fields are the
/// original frozen-contract shape; the trailing block exposes the management + P&amp;L state the
/// <c>PaperTrade</c> aggregate already carries (close reason, gross/net P&amp;L, costs, after-cost R, lifecycle,
/// live stop, exit price, risk budget, trigger timeframe, management edge, breakeven-armed) so the dashboard's
/// trades table can show every trade like a real (paper) trading platform. All money/price values are in the same
/// units the aggregate books; the new fields are nullable where they exist only after close.
/// </summary>
public sealed record PaperTradeDto(
    Guid Id,
    Guid SetupId,
    string Symbol,
    string Direction,
    string Status,
    string Style,
    string? Killzone,
    decimal Entry,
    decimal Stop,
    IReadOnlyList<decimal> Targets,
    decimal Size,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    decimal? RealizedR,
    // ---- Management + P&L state (already on the aggregate; appended so existing consumers stay valid) ----
    string Lifecycle,
    string? CloseReason,
    decimal? NetR,
    decimal? GrossPnl,
    decimal? Costs,
    decimal? NetPnl,
    bool HasScaledOut,
    bool IsBreakevenArmed,
    decimal RiskBudget,
    string Timeframe,
    decimal CurrentStop,
    decimal? ExitPrice,
    DateTimeOffset ManagedFromUtc);

// ---- Integration messages ----

/// <summary>Requests a SIMULATED trade from a confirmed setup. Routes only to SimulatedTradeExecutor.</summary>
public sealed record ExecutePaperTradeCommand(Guid SetupId) : ICommand;

public sealed record PaperTradeOpened(PaperTradeDto Trade) : IEvent;

public sealed record PaperTradeFilled(Guid TradeId, decimal Price, DateTimeOffset AtUtc) : IEvent;

public sealed record PaperTradeClosed(PaperTradeDto Trade, string Outcome) : IEvent;

public sealed record GetActiveTradesQuery : IQuery<IReadOnlyList<PaperTradeDto>>;

/// <summary>
/// Reads paper trades for the dashboard's trades table — every trade by default, or a single
/// <see cref="TradeStatus"/> (<c>Open</c>/<c>Closed</c>) and/or symbol. Read-only advisory projection (§6.3); the
/// returned DTOs carry no order field. <paramref name="Status"/>/<paramref name="Symbol"/> are case-insensitive;
/// an unrecognised status returns all trades.
/// </summary>
public sealed record GetTradesQuery(string? Status = null, string? Symbol = null)
    : IQuery<IReadOnlyList<PaperTradeDto>>;

/// <summary>
/// A read-only snapshot of the demo paper account for the dashboard's live-config panel: equity vs the configured
/// starting equity, the adaptive-risk peak/drawdown-trough and win/loss streaks (§2.4/§2.5.5), and the current
/// open-risk against the §2.5.10 portfolio cap. Advisory only — it routes nowhere near an order path (§6.3).
/// </summary>
public sealed record AccountStatusDto(
    decimal StartingEquity,
    decimal Equity,
    decimal PeakEquity,
    decimal DrawdownTrough,
    decimal OpenRisk,
    decimal OpenRiskCap,
    decimal RiskUtilizationPercent,
    decimal MaxOpenPortfolioRiskPercent,
    int ConsecutiveWins,
    int ConsecutiveLosses,
    int OpenTradeCount);

/// <summary>Reads the live demo-account status (plan §5.1/§5.3). Read-only — routes nowhere near an order path.</summary>
public sealed record GetAccountStatusQuery : IQuery<AccountStatusDto>;
