using IctTrader.Domain.Detection;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;

namespace IctTrader.Scanning.Application.Scanning.Models;

/// <summary>
/// The complete recipe for ONE setup/strategy model (plan §16): the detector pipeline it runs (in its pinned,
/// order-significant sequence) plus the option preset that expresses its confluence semantics (weights, required
/// conditions, FSM knobs). A <see cref="SymbolScanner"/> is composed FROM a definition — base options →
/// <see cref="ApplyPreset"/> (the model's deltas) → per-instrument overrides (instrument geometry always wins
/// last) → <see cref="BuildPipeline"/> over the fully-resolved options. Definitions are pure code (they ARE the
/// mined methodology, like detector defaults); operator-tunable deltas ride the existing options seams.
/// </summary>
public sealed record SetupModelDefinition
{
    public required SetupModel Id { get; init; }

    /// <summary>The operator-facing name (Settings/Signals badges), e.g. "ICT 2022 Mentorship".</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Overlays the model's option deltas onto the host's base snapshot. The canonical Ict2022 model is the
    /// identity function — the global <c>Ict:*</c> defaults ARE its mined parameters, so its pipeline stays
    /// byte-identical to the pre-catalog wiring.
    /// </summary>
    public required Func<ScannerOptions, ScannerOptions> ApplyPreset { get; init; }

    /// <summary>
    /// Builds the model's detector pipeline, in its PINNED canonical order, from the fully-resolved options
    /// (preset + instrument overrides already applied). The <see cref="NyClock"/> is the scanner's DST-aware
    /// session clock (shared with its <see cref="MarketContext"/>).
    /// </summary>
    public required Func<ScannerOptions, NyClock, IReadOnlyList<ISetupDetector>> BuildPipeline { get; init; }
}
