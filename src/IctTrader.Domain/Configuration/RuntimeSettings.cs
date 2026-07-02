using IctTrader.Domain.Instruments;
using IctTrader.Domain.Setups;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// The mutable, thread-safe RUNTIME settings the operator edits while the system is live (plan §15 — "settings in the
/// UI, applied without a restart"). It is seeded once from the validated <c>Ict:*</c> config at startup and then
/// updated through the settings API. A monotonic <see cref="Revision"/> ticks on every change so the per-symbol
/// scanner/orchestrator caches can detect a change and rebuild with the new settings (the live-apply seam). This
/// holds the PER-INSTRUMENT overrides now; the global concept knobs and the economic calendar are added to the same
/// store + revision in the following slices.
/// </summary>
public interface IRuntimeSettings
{
    /// <summary>Bumps on every settings change — consumers compare it to detect "something changed, rebuild".</summary>
    int Revision { get; }

    /// <summary>The current per-instrument overrides (symbol → overrides), case-insensitive by symbol.</summary>
    IReadOnlyDictionary<string, InstrumentOptionOverrides> InstrumentOverrides { get; }

    /// <summary>Sets (or, with <c>null</c>, clears) one symbol's overrides and bumps <see cref="Revision"/>.</summary>
    void SetInstrumentOverride(string symbol, InstrumentOptionOverrides? overrides);

    /// <summary>The operator's LIVE multi-select of active setup models (plan §16), or null when no live override
    /// is set (the scanner then uses the configured <c>Ict:Scanning:ActiveModels</c> default). Never empty.</summary>
    IReadOnlyList<SetupModel>? ActiveModelsOverride { get; }

    /// <summary>Sets (or, with null/empty, clears) the live active-model selection and bumps <see cref="Revision"/>
    /// so the scanner caches rebuild on the next candle (the same live-apply seam as instrument overrides).</summary>
    void SetActiveModels(IReadOnlyList<SetupModel>? models);
}

/// <summary>
/// Thread-safe <see cref="IRuntimeSettings"/> backed by copy-on-write snapshots (readers never lock; a writer swaps in
/// a fresh dictionary under a lock and bumps the revision). Registered as a singleton in the Host, seeded from
/// <see cref="InstrumentOverridesOptions"/>.
/// </summary>
public sealed class RuntimeSettings : IRuntimeSettings
{
    private readonly Lock _writeLock = new();
    private volatile IReadOnlyDictionary<string, InstrumentOptionOverrides> _instrumentOverrides;
    private volatile IReadOnlyList<SetupModel>? _activeModelsOverride;
    private int _revision;

    public RuntimeSettings(IReadOnlyDictionary<string, InstrumentOptionOverrides>? seed = null)
        => _instrumentOverrides = new Dictionary<string, InstrumentOptionOverrides>(
            seed ?? new Dictionary<string, InstrumentOptionOverrides>(), StringComparer.OrdinalIgnoreCase);

    public int Revision => Volatile.Read(ref _revision);

    public IReadOnlyDictionary<string, InstrumentOptionOverrides> InstrumentOverrides => _instrumentOverrides;

    public IReadOnlyList<SetupModel>? ActiveModelsOverride => _activeModelsOverride;

    public void SetActiveModels(IReadOnlyList<SetupModel>? models)
    {
        lock (_writeLock)
        {
            // Null/empty CLEARS the override (falls back to the configured default) — an operator can narrow or
            // widen the model set live, but can never "select nothing" into a silently dead scanner.
            _activeModelsOverride = models is { Count: > 0 } ? models.Distinct().ToArray() : null;
            Interlocked.Increment(ref _revision);
        }
    }

    public void SetInstrumentOverride(string symbol, InstrumentOptionOverrides? overrides)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        lock (_writeLock)
        {
            var next = new Dictionary<string, InstrumentOptionOverrides>(
                _instrumentOverrides, StringComparer.OrdinalIgnoreCase);
            if (overrides is null)
            {
                next.Remove(symbol);
            }
            else
            {
                next[symbol] = overrides;
            }

            _instrumentOverrides = next;
            Interlocked.Increment(ref _revision);
        }
    }
}
