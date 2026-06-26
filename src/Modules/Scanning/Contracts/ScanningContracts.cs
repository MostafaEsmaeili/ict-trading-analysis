using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Scanning.Contracts;

// ---- DTOs (camelCase JSON; plan §11.1 #4). Enum-like fields are strings for a stable, language-neutral
// wire contract (the dashboard depends on the exact names — Direction/Killzone/Style/Grade). ----

/// <summary>A confirmed, ADVISORY setup. <see cref="IsAdvisoryOnly"/> is always true (plan §6.3).</summary>
public sealed record SetupDto(
    Guid Id,
    string Symbol,
    string Direction,
    string Killzone,
    string Style,
    string Grade,
    string TriggerTimeframe,
    decimal Entry,
    decimal Stop,
    IReadOnlyList<decimal> Targets,
    decimal RewardRatio,
    string Reason,
    DateTimeOffset DetectedAtUtc,
    bool IsAdvisoryOnly);

public sealed record ScanStatusDto(string Symbol, string? ActiveKillzone, int OpenSetups);

// ---- Integration messages ----

public sealed record SetupConfirmed(SetupDto Setup) : IEvent;

public sealed record SetupRejected(string Symbol, string Reason, DateTimeOffset AtUtc) : IEvent;

public sealed record GetActiveKillzoneQuery(string Symbol) : IQuery<string?>;

public sealed record GetScanStatusQuery : IQuery<IReadOnlyList<ScanStatusDto>>;

/// <summary>
/// The most-recent <paramref name="Max"/> confirmed, advisory setups for a <paramref name="Symbol"/>, NEWEST-FIRST,
/// to overlay on the dashboard's ICT Pattern Chart (plan §9.1). Additive — the frozen REST wire
/// (<c>GET /api/chart/{symbol}</c>) is unchanged; this is the bus query the Host routes to.
/// </summary>
public sealed record GetRecentSetupsQuery(string Symbol, int Max) : IQuery<IReadOnlyList<SetupDto>>;
