using System.Collections.Concurrent;
using IctTrader.Scanning.Contracts;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// The Scanning module's recent-setup chart read-model: a bounded, thread-safe ring buffer of the most-recent
/// confirmed <see cref="SetupDto"/> per symbol (plan §3.0a / §9.1), feeding the dashboard's ICT Pattern Chart
/// overlays. The <see cref="SetupConfirmedChartProjectionHandler"/> <see cref="Add"/>s each confirmed setup; the
/// <see cref="GetRecentSetupsQueryHandler"/> reads a newest-first window via <see cref="Recent"/>. Registered as a
/// SINGLETON so the overlays survive across bus dispatches.
///
/// <para><b>Bounded:</b> each symbol holds at most <see cref="MaxSetupsPerSymbol"/> setups — once full, adding
/// evicts the OLDEST so the overlay store never grows without bound (the latest setups, not an audit log). The cap
/// is a named const, not a magic number.</para>
///
/// <para><b>Keyed by Symbol:</b> matching is case-insensitive (ordinal) so a query for "eurusd" finds setups
/// confirmed for "EURUSD".</para>
///
/// <para><b>Thread-safe:</b> the in-memory bus dispatches sequentially, but each per-symbol buffer guards with its
/// own lock anyway so it stays correct if a future distributed transport (plan §3.0a) fans handlers out
/// concurrently. Reads copy out a snapshot so a concurrent add can never tear the window the query projects.</para>
///
/// <para><b>Read-only sink (plan §6.3 guardrail):</b> these are advisory confirmed setups (<see cref="SetupDto.IsAdvisoryOnly"/>
/// is always true) — surfacing one as a chart overlay routes nowhere near an order path.</para>
///
/// <para><b>Deferred:</b> this is in-memory — a Host restart loses the overlays (it does not warm-start from a
/// persisted setup store). Persistence / warm-start of the overlay read-model is a follow-on.</para>
/// </summary>
public sealed class RecentSetupStore
{
    /// <summary>The per-symbol ring-buffer capacity: the maximum recent setups retained for one symbol.</summary>
    public const int MaxSetupsPerSymbol = 50;

    // One bounded buffer per symbol. ConcurrentDictionary makes get-or-add of a new symbol thread-safe; each
    // SymbolSetups guards its own list so concurrent adds to DIFFERENT symbols never contend.
    private readonly ConcurrentDictionary<string, SymbolSetups> _bySymbol =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Appends one confirmed setup to its symbol's buffer. If the buffer is at <see cref="MaxSetupsPerSymbol"/>, the
    /// OLDEST setup is evicted first so the bound holds. Thread-safe.
    /// </summary>
    public void Add(SetupDto setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        var bucket = _bySymbol.GetOrAdd(setup.Symbol, static _ => new SymbolSetups());
        bucket.Add(setup);
    }

    /// <summary>
    /// Returns up to <paramref name="max"/> of the most-recent setups for <paramref name="symbol"/>, NEWEST-FIRST
    /// (the latest confirmed setup at the top). A non-positive <paramref name="max"/>, or an unknown symbol, returns
    /// an empty list. The returned list is a snapshot copy — safe to enumerate while adds continue.
    /// </summary>
    public IReadOnlyList<SetupDto> Recent(string symbol, int max)
    {
        if (max <= 0 || symbol is null || !_bySymbol.TryGetValue(symbol, out var bucket))
        {
            return [];
        }

        return bucket.Recent(max);
    }

    /// <summary>One bounded, lock-guarded FIFO buffer for a single symbol's confirmed setups.</summary>
    private sealed class SymbolSetups
    {
        private readonly object _gate = new();

        // A list used as a FIFO ring: append to the tail (newest), evict from the head (oldest) past the cap.
        private readonly List<SetupDto> _setups = new(MaxSetupsPerSymbol);

        public void Add(SetupDto setup)
        {
            lock (_gate)
            {
                if (_setups.Count >= MaxSetupsPerSymbol)
                {
                    _setups.RemoveAt(0);
                }

                _setups.Add(setup);
            }
        }

        public IReadOnlyList<SetupDto> Recent(int max)
        {
            lock (_gate)
            {
                var take = Math.Min(max, _setups.Count);
                var window = new SetupDto[take];

                // Walk backward from the tail (newest) so the result is newest-first without a separate reverse.
                for (var i = 0; i < take; i++)
                {
                    window[i] = _setups[_setups.Count - 1 - i];
                }

                return window;
            }
        }
    }
}
