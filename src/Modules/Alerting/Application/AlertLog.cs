using IctTrader.Alerting.Contracts;

namespace IctTrader.Alerting.Application;

/// <summary>
/// The Alerting module's in-memory read-model: a bounded, thread-safe ring buffer of the most-recent
/// <see cref="AlertDto"/> notifications (plan §3.0a / §9). The setup/trade event handlers <see cref="Add"/>
/// to it; the <see cref="GetRecentAlertsQueryHandler"/> reads a newest-first window via <see cref="Recent"/>.
/// Registered as a SINGLETON so the feed survives across bus dispatches.
///
/// <para><b>Bounded:</b> the buffer holds at most <see cref="MaxAlerts"/> alerts — once full, adding evicts the
/// OLDEST so the Alerts feed never grows without bound (an alerter, not an audit log). The cap is a named const,
/// not a magic number.</para>
///
/// <para><b>Thread-safe:</b> the in-memory bus dispatches sequentially, but this guards with a lock anyway so it
/// stays correct if a future distributed transport (plan §3.0a) fans handlers out concurrently. Reads copy out a
/// snapshot so a concurrent add can never tear the window the query is projecting.</para>
///
/// <para><b>Read-only sink (plan §6.3 guardrail):</b> this is an advisory notification store — appending a
/// setup/trade notification routes nowhere near an order path.</para>
///
/// <para><b>Deferred:</b> this is in-memory — a Host restart loses the feed (it does not warm-start from the
/// persisted setup/trade rows). Persistence / warm-start of the alert feed is a follow-on.</para>
/// </summary>
public sealed class AlertLog
{
    /// <summary>The ring-buffer capacity: the maximum number of recent alerts retained for the feed.</summary>
    public const int MaxAlerts = 200;

    private readonly object _gate = new();

    // A simple list used as a FIFO ring: append to the tail, evict from the head past the cap. Capacity is
    // pre-sized to the cap so steady-state adds never reallocate.
    private readonly List<AlertDto> _alerts = new(MaxAlerts);

    /// <summary>
    /// Appends one alert to the feed. If the buffer is at <see cref="MaxAlerts"/>, the OLDEST alert is evicted
    /// first so the bound holds. Thread-safe.
    /// </summary>
    public void Add(AlertDto alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        lock (_gate)
        {
            if (_alerts.Count >= MaxAlerts)
            {
                _alerts.RemoveAt(0);
            }

            _alerts.Add(alert);
        }
    }

    /// <summary>
    /// Returns up to <paramref name="max"/> of the most-recent alerts, NEWEST-FIRST (so the dashboard feed shows
    /// the latest setup/trade at the top). A non-positive <paramref name="max"/> returns an empty list; a
    /// <paramref name="max"/> larger than the feed returns the whole feed. The returned list is a snapshot copy —
    /// safe to enumerate while adds continue.
    /// </summary>
    public IReadOnlyList<AlertDto> Recent(int max)
    {
        if (max <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            var take = Math.Min(max, _alerts.Count);
            var window = new AlertDto[take];

            // Walk backward from the tail (newest) so the result is newest-first without a separate reverse.
            for (var i = 0; i < take; i++)
            {
                window[i] = _alerts[_alerts.Count - 1 - i];
            }

            return window;
        }
    }
}
