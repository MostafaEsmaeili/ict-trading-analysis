using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Performance.Contracts;

// ---- DTOs (camelCase JSON; plan §11.1 #4 / §5.3) ----

public sealed record PerformanceSummaryDto(
    int TradeCount,
    decimal WinRate,
    decimal AverageR,
    decimal ProfitFactor,
    decimal Expectancy,
    decimal MaxDrawdown);

public sealed record EquityPointDto(DateTimeOffset AtUtc, decimal Equity);

// ---- Integration messages ----

public sealed record PerformanceUpdated(PerformanceSummaryDto Summary) : IEvent;

public sealed record GetPerformanceSummaryQuery : IQuery<PerformanceSummaryDto>;

public sealed record GetEquityCurveQuery : IQuery<IReadOnlyList<EquityPointDto>>;
