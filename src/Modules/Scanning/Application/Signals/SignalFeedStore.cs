using System.Collections.Concurrent;
using IctTrader.Domain.Configuration;
using IctTrader.Scanning.Contracts;

namespace IctTrader.Scanning.Application.Signals;

/// <summary>
/// The Scanning module's "best opportunities" read-model: a bounded, thread-safe in-memory store of the most-recent
/// confirmed advisory <see cref="SetupDto"/> across the WHOLE (symbol × timeframe × style) matrix (plan §3.0a / §9) —
/// the raw set the <see cref="SignalRankingService"/> ranks into the top-N feed. The
/// <c>SetupConfirmedSignalFeedHandler</c> <see cref="Add"/>s each confirmed setup; the ranking service reads a live
/// <see cref="Snapshot"/>. Registered as a SINGLETON so the feed survives across bus dispatches.
///
/// <para><b>Keyed by <see cref="SetupDto.Id"/>:</b> the id is deterministic (<c>SetupDtoMapper</c>), so a replayed or
/// redelivered candle that re-confirms the SAME setup is DE-DUPED — re-adding an existing id replaces in place rather
/// than duplicating the opportunity in the feed.</para>
///
/// <para><b>Recency-bounded:</b> a confirmed setup ages out of the feed once it is older than
/// <see cref="SignalRankingOptions.RecencyCutoffMinutes"/> relative to a caller-supplied "now" — a fleeting §2.5
/// intraday opportunity should not keep topping the board. Stale entries are pruned lazily on <see cref="Add"/> and
/// excluded from every <see cref="Snapshot"/>, so the store self-trims without a background timer (clock-free: the
/// caller passes <c>nowUtc</c>, mirroring the project's TimeProvider discipline — plan §4.8).</para>
///
/// <para><b>Size-bounded:</b> beyond recency, the store never holds more than
/// <see cref="SignalRankingOptions.MaxFeedSize"/> entries — once full, adding evicts the OLDEST by detection time so
/// the feed stays the latest opportunities, not an audit log.</para>
///
/// <para><b>Thread-safe:</b> the in-memory bus dispatches sequentially, but a single lock guards the map so the store
/// stays correct if a future distributed transport (plan §3.0a) fans handlers out concurrently; reads copy a
/// snapshot so a concurrent add can never tear the set the ranking service projects.</para>
///
/// <para><b>Read-only sink (plan §6.3 guardrail):</b> these are advisory confirmed setups
/// (<see cref="SetupDto.IsAdvisoryOnly"/> is always true) — surfacing one as a ranked signal routes nowhere near an
/// order path.</para>
///
/// <para><b>Deferred:</b> this is in-memory — a Host restart loses the feed (no warm-start). A future slice could
/// instead read persisted confirmed setups (plan §7) so the feed survives a restart; the ranking/filtering would be
/// unchanged.</para>
/// </summary>
public sealed class SignalFeedStore
{
    private readonly SignalRankingOptions _options;
    private readonly object _gate = new();

    // Keyed by the deterministic SetupDto.Id so a redelivered setup de-dupes (replace in place). A plain dictionary
    // under the lock is enough; the value is the confirmed setup.
    private readonly Dictionary<Guid, SetupDto> _byId = [];

    public SignalFeedStore(SignalRankingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Adds (or, for a redelivered id, replaces) one confirmed advisory setup, then prunes any entry older than the
    /// recency cutoff relative to <paramref name="nowUtc"/> and evicts the oldest if the size bound is exceeded.
    /// Thread-safe.
    /// </summary>
    public void Add(SetupDto setup, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(setup);

        lock (_gate)
        {
            _byId[setup.Id] = setup; // de-dupe by deterministic id (replace in place)
            PruneStale(nowUtc);
            EvictOverCap();
        }
    }

    /// <summary>
    /// Returns a snapshot of the LIVE (non-stale relative to <paramref name="nowUtc"/>) confirmed setups — the raw set
    /// the ranking service orders + filters. The returned list is a copy, safe to enumerate while adds continue.
    /// </summary>
    public IReadOnlyList<SetupDto> Snapshot(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            var cutoff = nowUtc - TimeSpan.FromMinutes(_options.RecencyCutoffMinutes);
            return _byId.Values.Where(s => s.DetectedAtUtc >= cutoff).ToList();
        }
    }

    private void PruneStale(DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc - TimeSpan.FromMinutes(_options.RecencyCutoffMinutes);
        var stale = _byId.Where(kv => kv.Value.DetectedAtUtc < cutoff).Select(kv => kv.Key).ToList();
        foreach (var id in stale)
        {
            _byId.Remove(id);
        }
    }

    private void EvictOverCap()
    {
        while (_byId.Count > _options.MaxFeedSize)
        {
            // Evict the OLDEST by detection time (then by id for a deterministic tiebreak) so the cap holds.
            var oldest = _byId
                .OrderBy(kv => kv.Value.DetectedAtUtc)
                .ThenBy(kv => kv.Key)
                .First().Key;
            _byId.Remove(oldest);
        }
    }
}
