using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Contracts;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// The bounded, thread-safe in-memory board of Manual-mode confirmed setups awaiting an operator TAKE (plan §15 — the
/// operator's "give me the opportunity to use that setup"). The <see cref="SetupConfirmedHandler"/> <see cref="Add"/>s
/// each Manual setup; the <c>TakeSetupCommandHandler</c> <see cref="TryTake"/>s it (look up + remove atomically); the
/// signals feed reads <see cref="IsPending"/> / <see cref="Get"/> to mark a signal as takeable. Registered as a
/// SINGLETON so the board survives across bus dispatches.
///
/// <para><b>In-memory by design (NOT DB).</b> A pending reserves no §2.5.10 risk and books nothing — it is not a
/// position, so it must NOT enter the DB-as-state position model (only opened trades / armed entries persist). It is a
/// transient watchlist; a Host restart drops it, exactly like the signals feed.</para>
///
/// <para><b>Keyed by the deterministic <see cref="SetupDto.Id"/>:</b> a replayed/redelivered candle that re-confirms
/// the SAME setup is DE-DUPED (re-adding an existing id replaces in place). That id is also the future trade id, so
/// taking is idempotent against the opened-trade idempotency guard.</para>
///
/// <para><b>Expiry (pure, clock-free — the caller passes <c>nowUtc</c>, plan §4.8).</b> A pending ages out once it is
/// older than <see cref="PendingOpportunityOptions.MaxPendingMinutes"/>, OR (when
/// <see cref="PendingOpportunityOptions.ExpireOnKillzoneEnd"/>) once <c>nowUtc</c> is no longer an active killzone
/// ENTRY for its instrument class (the §2.5.1-step-3 entry window closed — window over / lunch / index cutoff), reusing
/// the SAME <see cref="KillzoneClock"/> rule the entry detector + no-chase rung use so the take window can't drift from
/// the entry window. Stale entries are pruned lazily on every <see cref="Add"/>/<see cref="Snapshot"/>/lookup, so the
/// board self-trims without a background timer.</para>
///
/// <para><b>Read-only/advisory sink (plan §6.3 guardrail):</b> these are advisory confirmed setups
/// (<see cref="SetupDto.IsAdvisoryOnly"/> is always true) — holding one as a pending opportunity routes nowhere near an
/// order path; only an explicit operator TAKE opens a SIMULATED trade through the shared opener.</para>
/// </summary>
public sealed class PendingOpportunityStore
{
    private readonly PendingOpportunityOptions _options;
    private readonly KillzoneClock _killzoneClock;
    private readonly IReadOnlyCollection<Killzone> _activeKillzones;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, PendingOpportunity> _byId = [];

    public PendingOpportunityStore(
        PendingOpportunityOptions options,
        KillzoneClock killzoneClock,
        IReadOnlyCollection<Killzone> activeKillzones)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(killzoneClock);
        ArgumentNullException.ThrowIfNull(activeKillzones);
        _options = options;
        _killzoneClock = killzoneClock;
        _activeKillzones = activeKillzones;
    }

    /// <summary>
    /// Records (or, for a redelivered id, replaces) one Manual-mode pending opportunity, then prunes anything expired
    /// relative to <paramref name="nowUtc"/> and evicts the oldest if the size bound is exceeded. Thread-safe.
    /// </summary>
    internal void Add(PendingOpportunity pending, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(pending);

        lock (_gate)
        {
            _byId[pending.Id] = pending; // de-dupe by deterministic id (replace in place)
            Prune(nowUtc);
            EvictOverCap();
        }
    }

    /// <summary>
    /// Atomically looks up a non-expired pending by id and REMOVES it (the take consumes it). Returns null when the id
    /// is unknown OR has expired relative to <paramref name="nowUtc"/> (an expired pending is pruned and treated as
    /// gone). Thread-safe — concurrent takes of the same id resolve to exactly one winner.
    /// </summary>
    internal PendingOpportunity? TryTake(Guid id, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            Prune(nowUtc);
            if (_byId.Remove(id, out var pending))
            {
                return pending;
            }

            return null;
        }
    }

    /// <summary>Removes a pending by id (e.g. once it has been taken/opened on the auto-idempotency path). Thread-safe;
    /// a no-op when the id is absent.</summary>
    internal void Remove(Guid id)
    {
        lock (_gate)
        {
            _byId.Remove(id);
        }
    }

    /// <summary>Whether a non-expired pending exists for the id (the signals feed marks a signal takeable on this).</summary>
    public bool IsPending(Guid id, DateTimeOffset nowUtc) => Get(id, nowUtc) is not null;

    /// <summary>
    /// The AGE-based expiry instant of a non-expired pending (detection time + <c>MaxPendingMinutes</c>), or null when
    /// the id is not currently pending — the public take-state the signals feed surfaces as a wire countdown. The
    /// dynamic killzone-end expiry still prunes the pending server-side; this is the stable age window for the wire.
    /// </summary>
    public DateTimeOffset? AgeExpiryFor(Guid id, DateTimeOffset nowUtc)
    {
        var pending = Get(id, nowUtc);
        return pending is null
            ? null
            : pending.DetectedAtUtc.AddMinutes(_options.MaxPendingMinutes);
    }

    /// <summary>The non-expired pending for an id, or null (unknown/expired). Read-only; does not consume it.</summary>
    internal PendingOpportunity? Get(Guid id, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            Prune(nowUtc);
            return _byId.GetValueOrDefault(id);
        }
    }

    /// <summary>The live (non-expired relative to <paramref name="nowUtc"/>) pending opportunities — a copy, safe to
    /// enumerate while adds continue. Newest-first by detection time.</summary>
    internal IReadOnlyList<PendingOpportunity> Snapshot(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            Prune(nowUtc);
            return _byId.Values.OrderByDescending(p => p.DetectedAtUtc).ToList();
        }
    }

    /// <summary>The current number of live (non-expired) pending opportunities. Mainly for tests/diagnostics.</summary>
    public int Count(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            Prune(nowUtc);
            return _byId.Count;
        }
    }

    private void Prune(DateTimeOffset nowUtc)
    {
        var expired = _byId.Where(kv => IsExpired(kv.Value, nowUtc)).Select(kv => kv.Key).ToList();
        foreach (var id in expired)
        {
            _byId.Remove(id);
        }
    }

    private bool IsExpired(PendingOpportunity pending, DateTimeOffset nowUtc)
    {
        // Age expiry: older than the configured window relative to "now".
        if (nowUtc - pending.DetectedAtUtc > TimeSpan.FromMinutes(_options.MaxPendingMinutes))
        {
            return true;
        }

        // Killzone-end expiry: "now" is no longer an active killzone ENTRY for this instrument class (the §2.5.1-step-3
        // entry window closed). Reuses the same KillzoneClock rule the entry detector + no-chase rung use, so the take
        // window cannot drift from the entry window. Only when not already aged out and the policy is enabled.
        return _options.ExpireOnKillzoneEnd
            && !_killzoneClock.IsActiveEntry(nowUtc, pending.InstrumentClass, _activeKillzones);
    }

    private void EvictOverCap()
    {
        while (_byId.Count > _options.MaxPending)
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
