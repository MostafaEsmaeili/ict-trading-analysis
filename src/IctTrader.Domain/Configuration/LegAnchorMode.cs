namespace IctTrader.Domain.Configuration;

/// <summary>
/// How the displacement leg is anchored before the OTE band, the leg equilibrium, and the SD targets are
/// projected onto it (decision EG-1; plan §2.5.1 step 7 / §2.5.10 contradiction #1). The §2.5.10 default is
/// <see cref="BodyToBody"/> — ICT anchors the OTE fib on the candle bodies (Ep41: "for optimal trade entry I
/// like to use the bodies, lowest open or close in the swings"). <see cref="WickToWick"/> is the FOMC/NFP
/// exception (Ep41: on the FOMC "the extreme of the ranges should be considered ... I'm going to use the wick").
/// One leg, one anchor — every consumer of the leg inherits the choice, so the FVG-selection equilibrium and the
/// OTE entry can never drift onto different anchors.
/// </summary>
public enum LegAnchorMode
{
    /// <summary>Anchor on the candle bodies (origin = Open, terminus = Close) — the §2.5.10 default.</summary>
    BodyToBody,

    /// <summary>Anchor on the candle wicks (origin/terminus = Low/High) — the FOMC/NFP exception.</summary>
    WickToWick,
}
