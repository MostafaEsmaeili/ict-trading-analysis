namespace IctTrader.Domain.Sessions;

/// <summary>
/// A read-only source of scheduled economic events for the §2.5.2/§2.5.8 calendar no-trade gate (plan §6 feed
/// philosophy: read-only by SHAPE — a fetch returns events, there is no write/order surface). Concrete sources are
/// selected by config (an operator-supplied list, or an external provider over HTTP). The host's calendar loader
/// pumps the result into the <see cref="IEconomicCalendarStore"/>; the pure gate never talks to a source directly.
/// </summary>
public interface IEconomicCalendarSource
{
    /// <summary>A short identifier of the backing source (e.g. "Config", "Fmp") for operator-facing logs.</summary>
    string Provider { get; }

    /// <summary>
    /// Fetches the scheduled events overlapping the inclusive NY-date window. Implementations return ONLY the events
    /// the gate cares about (FOMC / NFP / CPI / other high-impact), NY-date keyed. Read-only — no order path.
    /// </summary>
    Task<IReadOnlyList<EconomicEvent>> FetchAsync(DateOnly fromNyDate, DateOnly toNyDate, CancellationToken ct = default);
}
