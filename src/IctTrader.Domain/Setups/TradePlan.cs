using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Setups;

/// <summary>
/// The tiered profit targets of a setup (plan §2.5.1 step 9): <see cref="Partial"/> (T1) and
/// <see cref="Runner"/> (T2, the draw on liquidity), ordered in the trade direction — bullish T1 &lt; T2,
/// bearish the mirror. Two tiers today; the shape leaves room for the SD-extension tiers later.
/// </summary>
public readonly record struct TargetLadder
{
    public TargetLadder(Direction direction, Price partial, Price runner)
    {
        var ordered = direction == Direction.Bullish
            ? partial.Value < runner.Value
            : partial.Value > runner.Value;
        Guard.Against(!ordered, "Target ladder T1 (partial) must sit before T2 (runner) in the trade direction.");
        Partial = partial;
        Runner = runner;
    }

    /// <summary>T1 — the partial-profit target.</summary>
    public Price Partial { get; }

    /// <summary>T2 — the runner target (the opposing draw on liquidity).</summary>
    public Price Runner { get; }
}

/// <summary>
/// A self-validating, ADVISORY priced trade plan (plan §2.5.1 steps 7–9, §3.0): direction, entry (the OTE array
/// level), stop (beyond the swept extreme), the tiered targets, and the reward-to-risk measured entry→runner.
/// The total price order is enforced — bullish <c>stop &lt; entry &lt; T1 &lt; T2</c>, bearish the mirror — so a
/// swapped stop/target can never form a plan. It prices nothing for execution; the paper-trade simulator consumes it.
/// </summary>
public readonly record struct TradePlan
{
    public TradePlan(Direction direction, Price entry, Price stop, TargetLadder targets)
    {
        var orderedLong = direction == Direction.Bullish
            && stop.Value < entry.Value
            && entry.Value < targets.Partial.Value
            && targets.Partial.Value < targets.Runner.Value;
        var orderedShort = direction == Direction.Bearish
            && stop.Value > entry.Value
            && entry.Value > targets.Partial.Value
            && targets.Partial.Value > targets.Runner.Value;
        Guard.Against(
            !(orderedLong || orderedShort),
            "TradePlan prices must run stop -> entry -> T1 -> T2 strictly in the trade direction.");

        Direction = direction;
        Entry = entry;
        Stop = stop;
        Targets = targets;

        var risk = Math.Abs(entry.Value - stop.Value);
        var reward = Math.Abs(targets.Runner.Value - entry.Value);
        RewardRatio = new RewardRatio(reward / risk);
    }

    public Direction Direction { get; }

    public Price Entry { get; }

    public Price Stop { get; }

    public TargetLadder Targets { get; }

    /// <summary>Reward-to-risk measured entry→runner (T2) — the §2.5 RR gate, recomputed from the geometry.</summary>
    public RewardRatio RewardRatio { get; }
}
