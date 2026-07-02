using IctTrader.Domain.Services;

namespace IctTrader.Performance.Application;

/// <summary>
/// The Performance module's accumulated read-model: the append-only stream of closed-trade R outcomes the
/// pure <see cref="PerformanceCalculator"/> folds into the §5.3 metrics + equity curve. Registered as a
/// SINGLETON so it survives across bus dispatches (the <see cref="PaperTradeClosedHandler"/> appends, the
/// query handlers read).
///
/// <para>The in-memory bus dispatches sequentially, but this guards with a lock anyway so it is correct even
/// if a future distributed transport (plan §3.0a) fans handlers out concurrently. Reads take a snapshot copy
/// so a concurrent append can never tear the list the calculator is iterating.</para>
///
/// <para><b>Deferred:</b> this is in-memory — a Host restart loses the history (it does not warm-start from the
/// persisted <c>PaperTrade</c> rows). Persistence / warm-start of the performance read-model is a follow-on
/// (plan §5.3 names a debounced snapshot cache + compute-on-read from the authoritative store).</para>
/// </summary>
public sealed class PerformanceState
{
    private readonly object _gate = new();
    private readonly List<Entry> _closed = [];

    /// <summary>Appends one closed trade's R outcome, tagged with the setup model that produced the trade
    /// (plan §16; null = unknown/pre-multi-model). Thread-safe.</summary>
    public void Record(ClosedTradeR trade, string? model = null)
    {
        lock (_gate)
        {
            _closed.Add(new Entry(trade, model));
        }
    }

    /// <summary>A point-in-time snapshot of the recorded closes — safe to iterate while appends continue.
    /// <paramref name="model"/> narrows to one setup model's trades (case-insensitive wire-name match); null
    /// returns every close (the frozen all-trades behavior).</summary>
    public IReadOnlyList<ClosedTradeR> Snapshot(string? model = null)
    {
        lock (_gate)
        {
            return _closed
                .Where(e => model is null || string.Equals(e.Model, model, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Trade)
                .ToArray();
        }
    }

    /// <summary>The distinct setup models recorded so far (insertion order), for the per-model breakdown.</summary>
    public IReadOnlyList<string> Models()
    {
        lock (_gate)
        {
            return _closed
                .Where(e => e.Model is not null)
                .Select(e => e.Model!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private readonly record struct Entry(ClosedTradeR Trade, string? Model);
}
