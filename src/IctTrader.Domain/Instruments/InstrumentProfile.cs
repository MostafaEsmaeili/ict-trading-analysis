using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Instruments;

/// <summary>
/// The resolved per-instrument profile (plan §2.5.7 caveat 3) — the price geometry (<see cref="SymbolSpec"/>),
/// the money geometry (<see cref="ContractSpec"/>), the <see cref="ValueObjects.InstrumentClass"/> that drives the
/// FX-vs-index killzone split, and the per-class <see cref="Overrides"/> the construction sites apply onto the
/// global <c>Ict:*</c> options. <see cref="IsKnown"/> distinguishes a configured instrument from the FX-default
/// fallback assigned to an unrecognised symbol (so a caller can log the fallback) — the fallback is deliberately
/// the existing FX-major behaviour, so an unknown symbol scans exactly as it did before this catalog existed.
///
/// <para>Pure value object: it carries no behaviour beyond exposing its parts and is built by the
/// <see cref="IInstrumentRegistry"/>. The class lives in <see cref="ValueObjects.SymbolSpec.InstrumentClass"/>;
/// this profile bundles the three specs so a single lookup yields everything a per-symbol scanner/sizer needs.</para>
/// </summary>
public sealed record InstrumentProfile
{
    public InstrumentProfile(
        Symbol symbol,
        SymbolSpec symbolSpec,
        ContractSpec contractSpec,
        InstrumentOptionOverrides overrides,
        bool isKnown)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(symbolSpec);
        ArgumentNullException.ThrowIfNull(contractSpec);
        ArgumentNullException.ThrowIfNull(overrides);
        Symbol = symbol;
        SymbolSpec = symbolSpec;
        ContractSpec = contractSpec;
        Overrides = overrides;
        IsKnown = isKnown;
    }

    public Symbol Symbol { get; }

    public SymbolSpec SymbolSpec { get; }

    public ContractSpec ContractSpec { get; }

    /// <summary>The instrument class — Fx or Index — that selects the §2.5.7 killzone schedule.</summary>
    public InstrumentClass InstrumentClass => SymbolSpec.InstrumentClass;

    /// <summary>The per-class scalar overrides applied onto the global options (FX = <see cref="InstrumentOptionOverrides.None"/>).</summary>
    public InstrumentOptionOverrides Overrides { get; }

    /// <summary>False when this profile is the FX-default fallback for an unrecognised symbol (caller may log it).</summary>
    public bool IsKnown { get; }
}
