using IctTrader.Domain.Detection;

namespace IctTrader.Domain.Setups;

/// <summary>
/// One scoring detector's positive verdict for the current candle, fed into the <see cref="SetupCandidate"/>
/// FSM. Pairs the emitted <see cref="ConfluenceCondition"/> with its <see cref="DetectorResult"/> (which
/// carries the direction, key level, reason fragment, and evidence). Non-scoring feeders (null condition)
/// never produce one.
/// </summary>
public readonly record struct ConfluenceMatch(ConfluenceCondition Condition, DetectorResult Result);
