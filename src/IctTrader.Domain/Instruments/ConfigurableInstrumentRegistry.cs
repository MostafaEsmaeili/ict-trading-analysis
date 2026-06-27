using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Instruments;

/// <summary>
/// An <see cref="IInstrumentRegistry"/> decorator that overlays the operator's LIVE per-instrument settings
/// (<see cref="IRuntimeSettings.InstrumentOverrides"/>, seeded from <c>Ict:Instruments</c> and editable at runtime)
/// on top of a built-in registry (the <see cref="InstrumentCatalog"/>). For a symbol with a configured override it
/// merges the override's non-null fields onto the catalog's resolved per-class
/// <see cref="InstrumentProfile.Overrides"/> — so a baked/edited tuning result (e.g. NAS100's required-condition
/// subset) is the symbol's default, while the catalog's built-in index geometry survives where the override is silent.
/// It reads the runtime store on EVERY resolve, so a freshly-built scanner/orchestrator picks up the current settings
/// (the live-apply seam; the per-symbol caches evict on <see cref="IRuntimeSettings.Revision"/> so existing ones
/// rebuild too). A symbol with no override passes through unchanged.
/// </summary>
public sealed class ConfigurableInstrumentRegistry : IInstrumentRegistry
{
    private readonly IInstrumentRegistry _inner;
    private readonly IRuntimeSettings _settings;

    public ConfigurableInstrumentRegistry(IInstrumentRegistry inner, IRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(settings);
        _inner = inner;
        _settings = settings;
    }

    public InstrumentProfile Resolve(Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var profile = _inner.Resolve(symbol);
        // The store keys symbols case-insensitively, so the normalised Symbol.Value matches an operator's entry.
        if (!_settings.InstrumentOverrides.TryGetValue(symbol.Value, out var configOverride))
        {
            return profile; // no override for this symbol — the built-in catalog profile, unchanged
        }

        var merged = profile.Overrides.OverlayWith(configOverride);
        return new InstrumentProfile(
            profile.Symbol, profile.SymbolSpec, profile.ContractSpec, merged, profile.IsKnown);
    }
}
