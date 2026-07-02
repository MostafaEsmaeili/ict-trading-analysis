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

// ADDITIVE (plan §16): the optional Model filter narrows the metrics to trades produced by ONE setup model
// (a SetupModel member name); null keeps the frozen all-trades behavior byte-identical.
public sealed record GetPerformanceSummaryQuery(string? Model = null) : IQuery<PerformanceSummaryDto>;

public sealed record GetEquityCurveQuery(string? Model = null) : IQuery<IReadOnlyList<EquityPointDto>>;

/// <summary>One setup model's own §5.3 summary — the per-model breakdown row (plan §16) so the operator can
/// compare models ("which setup performs best") without losing the global headline aggregate.</summary>
public sealed record ModelPerformanceDto(string Model, int TradeCount, PerformanceSummaryDto Summary);

public sealed record GetModelPerformanceQuery : IQuery<IReadOnlyList<ModelPerformanceDto>>;
