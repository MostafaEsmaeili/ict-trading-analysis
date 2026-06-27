namespace IctTrader.Host;

/// <summary>One scheduled economic event on the wire (plan §2.5.8) — its NY date (ISO) and type, plus whether that
/// date is a §2.5.2 no-trade day under the current blackout policy. Read-only/advisory.</summary>
public sealed record CalendarEventDto(string Date, string Type, bool IsBlackout);

/// <summary>
/// The economic-calendar status for the dashboard (plan §15): whether the feed is enabled + has loaded, the source
/// provider, the NY-date window, the upcoming events (each flagged if its date is blacked out), and the full set of
/// blackout NY dates in the window so the UI can mark the no-trade days. Read-only — surfacing the calendar routes
/// nowhere near an order path (§6.3).
/// </summary>
public sealed record CalendarStatusDto(
    bool Enabled,
    bool Loaded,
    string Provider,
    string FromDate,
    string ToDate,
    IReadOnlyList<CalendarEventDto> Events,
    IReadOnlyList<string> BlackoutDates);
