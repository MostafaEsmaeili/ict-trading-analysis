using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Scanning.Contracts;

// ---- DTOs (camelCase JSON; plan §11.1 #4). Enum-like fields are strings for a stable, language-neutral
// wire contract (the dashboard depends on the exact names — Direction/Killzone/Style/Grade). ----

/// <summary>
/// A confirmed, ADVISORY setup. <see cref="IsAdvisoryOnly"/> is always true (plan §6.3).
/// <para><b>ADDITIVE (frozen-wire safe):</b> <see cref="Score"/> — the 0–100 confluence score (plan §2.5.4) the
/// confluence FSM produced at confirm time — is appended LAST as an optional record parameter (default 0). Existing
/// producers/consumers and the deterministic <see cref="Id"/> hash are unaffected (the id never hashes the score); a
/// path that does not carry a score (e.g. a hand-built test fixture) simply leaves it 0. The score is what the
/// Signals ranking + feed slice sorts on within a grade ("the system suggests the best setup").</para>
/// <para><b>ADDITIVE (frozen-wire safe):</b> <see cref="Model"/> — the setup model that confirmed this setup
/// (a <c>SetupModel</c> member name, plan §16) — is appended AFTER <see cref="Score"/> with the canonical
/// "Ict2022" default, so every pre-multi-model producer/consumer and fixture stays valid. For the DEFAULT
/// model the deterministic <see cref="Id"/> is UNCHANGED (replay idempotency vs existing paper trades); a
/// non-default model appends its name to the id hash so two models confirming the same bar can never collide.</para>
/// </summary>
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
    bool IsAdvisoryOnly,
    int Score = 0,
    string Model = "Ict2022");

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

// ---- Live "engine view" chart geometry (plan §9.1) — the concepts the scanner is tracking RIGHT NOW ----

/// <summary>
/// One live ICT-concept geometry overlay for the chart's "engine view" (plan §9.1): a snapshot of what the scanner's
/// <c>MarketContext</c> is currently tracking on a (symbol, timeframe), so the operator can SEE which concepts are
/// active/detected even between confirmed setups. Purely a read projection of live working memory — ADVISORY, it
/// routes nowhere near an order path (plan §6.3).
///
/// <para>A flat, kind-discriminated record: <see cref="Kind"/> selects which fields apply (the rest stay null):
/// <list type="bullet">
/// <item><c>"fvg"</c> / <c>"orderBlock"</c> / <c>"ote"</c> — a price BOX: <see cref="Top"/> + <see cref="Bottom"/>
/// (+ <see cref="Mid"/> = the OB 50% mean threshold, or the OTE 70.5% sweet spot). <see cref="State"/> carries the
/// FVG/OB lifecycle ("Open"/"Mitigated"/…).</item>
/// <item><c>"sweep"</c> / <c>"mss"</c> — a point-in-time MARKER at <see cref="AtUtc"/> and level <see cref="Price"/>
/// (the swept level / the broken swing).</item>
/// <item><c>"liquidity"</c> — a resting pool line at <see cref="Price"/> with <see cref="Side"/> ("BuySide"/"SellSide"),
/// a <see cref="Swept"/> flag, and <see cref="Strength"/> (the equal-touch cluster size, for the label).</item>
/// </list>
/// <see cref="Direction"/> is the wire enum name ("Bullish"/"Bearish"), or empty where a concept has none. Prices are
/// decimals at the instrument's own precision; <see cref="AtUtc"/> is UTC.</para>
/// </summary>
public sealed record GeometryOverlayDto(
    string Kind,
    string Direction,
    DateTimeOffset AtUtc,
    decimal? Price = null,
    decimal? Top = null,
    decimal? Bottom = null,
    decimal? Mid = null,
    string? State = null,
    string? Side = null,
    bool? Swept = null,
    int? Strength = null);

/// <summary>
/// The live geometry the scanner is currently tracking for a (<paramref name="Symbol"/>, <paramref name="Timeframe"/>)
/// — the "engine view" the ICT Pattern Chart draws under its concept toggles, capped at <paramref name="Max"/> per
/// concept. Additive bus query behind the frozen REST wire (<c>GET /api/chart/{symbol}</c>); read-only/advisory.
/// </summary>
public sealed record GetGeometryOverlaysQuery(
    string Symbol,
    string Timeframe,
    int Max,
    // ADDITIVE (plan §16): which setup model's engine view to read (a SetupModel member name); null = the canonical
    // Ict2022 view, so the pre-multi-model chart wire is unchanged.
    string? Model = null)
    : IQuery<IReadOnlyList<GeometryOverlayDto>>;

// ---- Signals ranking + feed (the "best opportunities" feed) — ADDITIVE, advisory, read-only (plan §6.3) ----

/// <summary>
/// One ranked, ADVISORY signal in the cross-matrix "best opportunities" feed: the confirmed <see cref="Setup"/> plus
/// its 1-based <see cref="Rank"/> (1 = the single best opportunity right now) and its 0–100 confluence
/// <see cref="Score"/> (mirrors <see cref="SetupDto.Score"/> for convenience — the same number the ranker sorts on
/// within a grade). The feed ranks across the whole (symbol × timeframe × style) matrix so the operator sees the best
/// setup first ("the system suggests the best setup"). Read-only — it routes nowhere near an order path.
/// <para><b>Take-state (the semi-auto TAKE workflow, plan §15) — appended as OPTIONAL record parameters AFTER
/// <see cref="Score"/> so the wire stays frozen-safe (exactly how <see cref="Score"/> was added; do NOT reorder/remove
/// the leading parameters):</b>
/// <list type="bullet">
/// <item><see cref="EntryMode"/> — the symbol's effective TAKE workflow ("Auto" / "Manual", a <c>TradeEntryMode</c>
/// member name). A Manual signal is one the operator can TAKE; an Auto one opens automatically.</item>
/// <item><see cref="IsTaken"/> — true once a paper trade (or armed entry) already exists under this signal's
/// deterministic id (auto-opened or already taken) — the UI shows it as acted-on, not takeable.</item>
/// <item><see cref="BlockReason"/> — null when the signal is takeable; otherwise a short reason the UI shows + uses to
/// disable the Take button (e.g. "Auto" / "AlreadyTaken" / "Expired" — and reserved for future "RiskCapReached").</item>
/// <item><see cref="ExpiresAtUtc"/> — when the pending opportunity ages out (ISO-8601 UTC), or null when it does not
/// apply (Auto, already taken, or no pending) — the UI can show a countdown.</item>
/// </list>
/// All four default to a takeable-unknown state so an existing producer (a hand-built fixture, the live-push handler
/// before the take wiring) stays valid. They are advisory display state only — none routes near an order path (§6.3).</para>
/// </summary>
public sealed record RankedSignalDto(
    int Rank,
    int Score,
    SetupDto Setup,
    string EntryMode = "Auto",
    bool IsTaken = false,
    string? BlockReason = null,
    string? ExpiresAtUtc = null);

/// <summary>
/// The ranked, filtered "best opportunities" feed (plan §9 / the Signals slice). Optional filters narrow the matrix:
/// <paramref name="Symbol"/> / <paramref name="Style"/> (case-insensitive exact match on the wire enum names),
/// <paramref name="MinGrade"/> (a floor — A returns only A, B returns A+B; defaults to the configured
/// <c>Ict:Signals:MinGrade</c>), and <paramref name="Max"/> (cap the top-N; defaults to the configured
/// <c>Ict:Signals:MaxFeedSize</c>). The Host routes <c>GET /api/signals</c> to this; read-only/advisory.
/// </summary>
public sealed record GetSignalsQuery(
    string? Symbol = null,
    string? Style = null,
    string? MinGrade = null,
    int? Max = null,
    // ADDITIVE: narrow to one setup model (a SetupModel member name, plan §16); null = all active models.
    string? Model = null)
    : IQuery<IReadOnlyList<RankedSignalDto>>;

/// <summary>
/// Pushed whenever the ranked feed changes (a new confirmed setup entered the matrix): the recomputed ranked top-N
/// (<paramref name="Top"/>), so the dashboard's Signals feed updates live over SignalR. Additive integration event —
/// read-only/advisory, it routes nowhere near an order path (plan §6.3).
/// </summary>
public sealed record SignalsUpdated(IReadOnlyList<RankedSignalDto> Top) : IEvent;
