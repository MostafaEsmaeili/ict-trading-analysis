using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Alerting.Contracts;

// ---- DTOs (camelCase JSON; plan §11.1 #4 / §9). Alerting subscribes to setup/trade/performance events
// and pushes to the dashboard; it publishes none of its own. ----

public sealed record AlertDto(
    Guid Id,
    string Kind,
    string Symbol,
    string Message,
    string? Direction,
    string? Killzone,
    string? Style,
    DateTimeOffset AtUtc);

public sealed record GetRecentAlertsQuery(int Max) : IQuery<IReadOnlyList<AlertDto>>;
