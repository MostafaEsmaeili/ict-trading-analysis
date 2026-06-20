namespace IctTrader.Domain.Sessions;

/// <summary>A high-impact economic event type relevant to the §2.5.2 calendar no-trade gate.</summary>
public enum CalendarEventType
{
    Fomc,
    Nfp,
    Cpi,
    Other,
}

/// <summary>
/// A scheduled economic event on a New-York calendar date (plan §2.5.2/§2.5.8). The pure
/// <c>CalendarGateDetector</c> consumes these from the <c>MarketContext</c>; the host/ingestion sources them
/// (the future EconomicCalendarFilter). Date-keyed in NY time so the blocking rules are host-zone-independent.
/// </summary>
public readonly record struct EconomicEvent(DateOnly NyDate, CalendarEventType Type);
