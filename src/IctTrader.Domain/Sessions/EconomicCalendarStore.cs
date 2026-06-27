namespace IctTrader.Domain.Sessions;

/// <summary>
/// The mutable, thread-safe holder of the current economic-calendar events the §2.5.2 no-trade gate reads (plan
/// §2.5.8). A background loader refreshes it from an <see cref="IEconomicCalendarSource"/>; a monotonic
/// <see cref="Revision"/> ticks on every refresh so a per-symbol scanner can detect a change and re-load the events
/// into its <c>MarketContext</c> (the live-feed seam, mirroring the runtime-settings revision pattern). Until a load
/// happens <see cref="IsLoaded"/> is false and the gate fails per its configured posture (default fail-open).
/// </summary>
public interface IEconomicCalendarStore
{
    /// <summary>Bumps on every successful load — a scanner compares it to detect "events changed, re-load".</summary>
    int Revision { get; }

    /// <summary>Whether a load has happened (distinguishes "no events" from "not yet sourced", as the gate does).</summary>
    bool IsLoaded { get; }

    /// <summary>The current scheduled events, NY-date keyed (an immutable snapshot; readers never lock).</summary>
    IReadOnlyList<EconomicEvent> Events { get; }

    /// <summary>Replaces the event set with a fresh snapshot, marks it loaded, and bumps <see cref="Revision"/>.</summary>
    void Load(IEnumerable<EconomicEvent> events);
}

/// <summary>
/// Thread-safe <see cref="IEconomicCalendarStore"/> backed by copy-on-write snapshots (readers never lock; the loader
/// swaps in a fresh list under a lock and bumps the revision). Registered as a singleton in the Host and refreshed by
/// the <c>EconomicCalendarLoaderHostedService</c>.
/// </summary>
public sealed class EconomicCalendarStore : IEconomicCalendarStore
{
    private readonly Lock _writeLock = new();
    private volatile IReadOnlyList<EconomicEvent> _events = [];
    private volatile bool _isLoaded;
    private int _revision;

    public int Revision => Volatile.Read(ref _revision);

    public bool IsLoaded => _isLoaded;

    public IReadOnlyList<EconomicEvent> Events => _events;

    public void Load(IEnumerable<EconomicEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        lock (_writeLock)
        {
            _events = events.ToArray();
            _isLoaded = true;
            Interlocked.Increment(ref _revision);
        }
    }
}
