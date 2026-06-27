using IctTrader.Domain.Instruments;
using IctTrader.Domain.Styles;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable draw-on-liquidity / reward-to-risk gate (plan §2.5.1 steps 2/8/9, §2.5.3 DrawTargetRR≥2.5). It does
/// NOT own an RR number — the floor is the active style's <c>MinRewardRatio</c> (already clamped by the hard
/// <see cref="TradeStyleOptions.AbsoluteMinRewardRatio"/>), read from <see cref="TradeStyleOptions"/>, so the
/// 2.5R default has a single source. THIS SLICE draws to registered untapped liquidity pools only; the broader
/// §2.5.1-step-2 draw set (prior-day H/L, HTF FVG, big figures) is a documented deferred subset (spec §5).
/// Bound from <c>Ict:Detection:DrawOnLiquidity</c>.
/// </summary>
public sealed class DrawOnLiquidityOptions
{
    public const string SectionName = "Ict:Detection:DrawOnLiquidity";

    /// <summary>Stop placed this many pips beyond the swept swing extreme (§2.5.5 ~10 pips FX; 1–2 ticks index).</summary>
    public decimal StopBufferPips { get; init; } = 10m;

    /// <summary>A candidate target within this many pips of the just-swept level is excluded (consumed liquidity).</summary>
    public decimal SweptLevelExclusionPips { get; init; } = 1.5m;

    /// <summary>Which trade style's MinRewardRatio floor applies (single-source RR; default Intraday = the §2.5 model).</summary>
    public TradeStyle Style { get; init; } = TradeStyle.Intraday;

    /// <summary>
    /// Returns a copy with the instrument-class scalar overrides applied where present
    /// (<see cref="StopBufferPips"/> + <see cref="SweptLevelExclusionPips"/>). A
    /// <see cref="InstrumentOptionOverrides.None"/> / FX bundle leaves both at their global value (byte-identical).
    /// The RR <see cref="Style"/> is instrument-agnostic and unchanged.
    /// </summary>
    public DrawOnLiquidityOptions WithInstrumentOverrides(InstrumentOptionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return new DrawOnLiquidityOptions
        {
            StopBufferPips = overrides.StopBufferPips ?? StopBufferPips,
            SweptLevelExclusionPips = overrides.SweptLevelExclusionPips ?? SweptLevelExclusionPips,
            Style = Style,
        };
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (StopBufferPips < 0m)
        {
            errors.Add($"StopBufferPips cannot be negative but was {StopBufferPips}.");
        }

        if (SweptLevelExclusionPips < 0m)
        {
            errors.Add($"SweptLevelExclusionPips cannot be negative but was {SweptLevelExclusionPips}.");
        }

        if (!Enum.IsDefined(Style))
        {
            errors.Add($"Style must be a defined TradeStyle but was {(int)Style}.");
        }

        return errors;
    }
}
