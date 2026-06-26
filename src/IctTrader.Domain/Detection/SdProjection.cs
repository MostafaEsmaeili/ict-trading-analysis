using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection;

/// <summary>One standard-deviation projection target (TGR-1): the multiple of the displacement-leg length and the
/// resulting price, projected beyond the leg terminus in the draw direction.</summary>
public readonly record struct SdTier(decimal Multiple, decimal Price);

/// <summary>
/// The standard-deviation projection targets of the current displacement leg (decision TGR-1/2; plan §2.5.10 #5).
/// One SD unit = the leg length (body-to-body, EG-1), and each tier is <c>Terminus + s × n × legLength</c> projected
/// in the draw direction — provably the SAME axis the OTE entry retraces on (<see cref="MarketStructure.Displacement.Project"/>),
/// the TGR-2 single-source invariant. Pure target geometry — NON-scoring (no <see cref="ConfluenceCondition"/>, no weight).
/// </summary>
public readonly record struct SdProjection(
    Direction Direction,
    decimal LegLength,
    decimal Terminus,
    IReadOnlyList<SdTier> Tiers);
