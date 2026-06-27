using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Instruments;

/// <summary>
/// Maps a normalised <see cref="Symbol"/> to its <see cref="InstrumentProfile"/> (plan §2.5.7 caveat 3) — the
/// single seam through which every per-symbol construction site (scanner, sizer, trade orchestrator) learns an
/// instrument's class + geometry + cost, replacing the former hardcoded <c>SymbolSpec.FxMajor</c>. An
/// unrecognised symbol resolves to the FX-default profile (<see cref="InstrumentProfile.IsKnown"/> = false) so
/// today's behaviour is preserved for symbols not yet catalogued. Pure and deterministic — no I/O, no clock.
/// </summary>
public interface IInstrumentRegistry
{
    /// <summary>Resolves the profile for a symbol; never null (falls back to FX-default for an unknown symbol).</summary>
    InstrumentProfile Resolve(Symbol symbol);
}
