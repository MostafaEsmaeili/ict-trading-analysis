using System.Collections.Concurrent;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Contracts;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// The Scanning module's live "engine view" chart read-model (plan §9.1): the LATEST geometry snapshot per
/// (symbol, timeframe) — the concepts the scanner is currently tracking (open FVGs / order blocks / liquidity pools,
/// the latest sweep / MSS, the OTE band). The <see cref="CandleIngestedHandler"/> <see cref="Set"/>s a fresh
/// immutable snapshot after each candle it scans; the <see cref="GetGeometryOverlaysQueryHandler"/> reads it via
/// <see cref="Get"/>. Registered a SINGLETON so the view survives across bus dispatches.
///
/// <para><b>Latest-wins:</b> unlike the recent-setup ring buffer this keeps only the CURRENT snapshot per
/// (symbol, timeframe) — the engine view is "what is active now", not a history. Each snapshot is an immutable list
/// built on the scan thread, so a concurrent HTTP read gets a whole, tear-free snapshot (no lock needed —
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> swaps the reference atomically).</para>
///
/// <para><b>Read-only sink (plan §6.3 guardrail):</b> these are advisory projections of detector state — surfacing
/// them as chart overlays routes nowhere near an order path.</para>
/// </summary>
public sealed class GeometryOverlayStore
{
    // Keyed "SYMBOL|TIMEFRAME|MODEL" (plan §16 — two active models on one cell must not overwrite each other's
    // engine view); the dictionary comparer is case-insensitive so "eurusd|M5|ict2022" finds "EURUSD|M5|Ict2022".
    private readonly ConcurrentDictionary<string, IReadOnlyList<GeometryOverlayDto>> _byKey =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The wire name of the model an overlay set belongs to when the caller does not say — the
    /// canonical model, so the pre-multi-model chart keeps reading the same view.</summary>
    public const string DefaultModel = nameof(SetupModel.Ict2022);

    /// <summary>Replaces the current snapshot for a (symbol, timeframe, model) with <paramref name="overlays"/> (latest-wins).</summary>
    public void Set(string symbol, Timeframe timeframe, IReadOnlyList<GeometryOverlayDto> overlays, SetupModel model = SetupModel.Ict2022)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        _byKey[Key(symbol, timeframe.ToString(), model.ToString())] = overlays ?? [];
    }

    /// <summary>
    /// The current geometry snapshot for a (symbol, timeframe, model), capped at <paramref name="max"/> overlays. A
    /// non-positive <paramref name="max"/>, a null symbol/timeframe, or an unknown key returns empty; a null/empty
    /// <paramref name="model"/> reads the canonical model's view.
    /// </summary>
    public IReadOnlyList<GeometryOverlayDto> Get(string symbol, string timeframe, int max, string? model = null)
    {
        if (max <= 0 || string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(timeframe)
            || !_byKey.TryGetValue(Key(symbol, timeframe, string.IsNullOrEmpty(model) ? DefaultModel : model), out var overlays))
        {
            return [];
        }

        return max >= overlays.Count ? overlays : overlays.Take(max).ToList();
    }

    private static string Key(string symbol, string timeframe, string model) => $"{symbol}|{timeframe}|{model}";
}
