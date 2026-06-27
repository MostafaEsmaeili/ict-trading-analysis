using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Instruments;

/// <summary>
/// An <see cref="IInstrumentRegistry"/> decorator that overlays operator config (<c>Ict:Instruments</c>) on top of a
/// built-in registry (the <see cref="InstrumentCatalog"/>). For a symbol with a configured override it merges the
/// config's non-null fields onto the catalog's resolved per-class <see cref="InstrumentProfile.Overrides"/> — so a
/// baked tuning result (e.g. NAS100 → 6-of-8) becomes the symbol's LIVE default while the catalog's built-in index
/// geometry survives where config is silent. A symbol with no config entry passes through unchanged (byte-identical).
/// Pure + thread-safe (immutable after construction); the lookup is case-insensitive on the dashboard symbol.
/// </summary>
public sealed class ConfigurableInstrumentRegistry : IInstrumentRegistry
{
    private readonly IInstrumentRegistry _inner;
    private readonly IReadOnlyDictionary<string, InstrumentOptionOverrides> _overrides;

    public ConfigurableInstrumentRegistry(
        IInstrumentRegistry inner, IReadOnlyDictionary<string, InstrumentOptionOverrides> overrides)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(overrides);
        _inner = inner;
        // Case-insensitive so an operator's "nas100usd" matches the normalised "NAS100USD" symbol key.
        _overrides = new Dictionary<string, InstrumentOptionOverrides>(overrides, StringComparer.OrdinalIgnoreCase);
    }

    public InstrumentProfile Resolve(Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var profile = _inner.Resolve(symbol);
        if (!_overrides.TryGetValue(symbol.Value, out var configOverride))
        {
            return profile; // no config for this symbol — the built-in catalog profile, unchanged
        }

        var merged = profile.Overrides.OverlayWith(configOverride);
        return new InstrumentProfile(
            profile.Symbol, profile.SymbolSpec, profile.ContractSpec, merged, profile.IsKnown);
    }
}
