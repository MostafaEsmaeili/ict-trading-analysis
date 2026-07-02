using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Alerting.Contracts;

// ---- DTOs (camelCase JSON; plan §11.1 #4 / §9). Alerting subscribes to setup/trade/performance events
// and pushes to the dashboard; it publishes none of its own. ----

/// <summary>ADDITIVE (frozen-wire safe): <see cref="AlertDto.Model"/> — the setup model that produced the
/// alert's setup/trade (a <c>SetupModel</c> member name, plan §16) — is appended LAST with a null default so
/// existing producers/consumers stay valid (null = unknown/pre-multi-model).</summary>
public sealed record AlertDto(
    Guid Id,
    string Kind,
    string Symbol,
    string Message,
    string? Direction,
    string? Killzone,
    string? Style,
    DateTimeOffset AtUtc,
    string? Model = null);

public sealed record GetRecentAlertsQuery(int Max) : IQuery<IReadOnlyList<AlertDto>>;
