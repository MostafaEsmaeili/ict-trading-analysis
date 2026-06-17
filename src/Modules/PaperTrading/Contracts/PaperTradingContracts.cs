using IctTrader.SharedKernel.Messaging;

namespace IctTrader.PaperTrading.Contracts;

// ---- DTOs (camelCase JSON; plan §11.1 #4) ----

/// <summary>A simulated trade. There is no live counterpart anywhere in the system (plan §6.3).</summary>
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
    decimal? RealizedR);

// ---- Integration messages ----

/// <summary>Requests a SIMULATED trade from a confirmed setup. Routes only to SimulatedTradeExecutor.</summary>
public sealed record ExecutePaperTradeCommand(Guid SetupId) : ICommand;

public sealed record PaperTradeOpened(PaperTradeDto Trade) : IEvent;

public sealed record PaperTradeFilled(Guid TradeId, decimal Price, DateTimeOffset AtUtc) : IEvent;

public sealed record PaperTradeClosed(PaperTradeDto Trade, string Outcome) : IEvent;

public sealed record GetActiveTradesQuery : IQuery<IReadOnlyList<PaperTradeDto>>;
