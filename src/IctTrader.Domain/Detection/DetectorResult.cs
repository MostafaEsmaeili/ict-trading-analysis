using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection;

/// <summary>
/// One detector's verdict for one candle (plan §4.2). Immutable + structurally equal so determinism tests
/// are trivial. <see cref="NoMatch"/> is the canonical "nothing formed" sentinel — detectors return it
/// rather than throwing on a small window or absent pattern.
/// </summary>
public readonly record struct DetectorResult(
    bool Matched,
    Direction? Direction,
    decimal? KeyLevel,
    string ReasonFragment,
    IReadOnlyDictionary<string, object>? Evidence)
{
    /// <summary>The canonical empty result: nothing formed this candle.</summary>
    public static DetectorResult NoMatch { get; } = new(false, null, null, string.Empty, null);

    /// <summary>Builds a positive match. <paramref name="reasonFragment"/> is rendered from a resource template.</summary>
    public static DetectorResult Match(
        Direction? direction,
        decimal? keyLevel,
        string reasonFragment,
        IReadOnlyDictionary<string, object>? evidence = null)
        => new(true, direction, keyLevel, reasonFragment, evidence);
}
