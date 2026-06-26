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
    private readonly List<ClosedTradeR> _closed = [];

    /// <summary>Appends one closed trade's R outcome. Thread-safe.</summary>
    public void Record(ClosedTradeR trade)
    {
        lock (_gate)
        {
            _closed.Add(trade);
        }
    }

    /// <summary>A point-in-time snapshot of the recorded closes — safe to iterate while appends continue.</summary>
    public IReadOnlyList<ClosedTradeR> Snapshot()
    {
        lock (_gate)
        {
            return _closed.ToArray();
        }
    }
}
