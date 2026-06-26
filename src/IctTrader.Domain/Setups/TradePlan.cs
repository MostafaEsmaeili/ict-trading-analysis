using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Setups;

/// <summary>
/// The tiered profit targets of a setup (plan §2.5.1 step 9): an ordered ladder T1..Tn running in the trade
/// direction — bullish strictly ascending, bearish strictly descending. <see cref="Partial"/> (T1) is the partial
/// target, <see cref="Runner"/> (Tn) the final runner (the reward-to-risk target). Two tiers historically (the
/// equilibrium partial + the draw-on-liquidity runner); TGR-1/2 appends the standard-deviation projection tiers.
/// </summary>
public readonly record struct TargetLadder
{
    /// <summary>
    /// The runner tier's index in the canonical ladder order (T1 at 0, the reward-to-risk runner next, deeper
    /// SD tiers after). This is the single source of truth the producer (<c>SetupFactory</c>) and the scan→trade
    /// wire consumer (<c>SetupRehydrator</c>) BOTH reference, so the tier the RR is measured to can never drift
    /// between them across the lossy <c>SetupDto</c> (which carries the ordered targets but not the runner index).
    /// </summary>
    public const int CanonicalRunnerIndex = 1;

    private readonly IReadOnlyList<Price> _targets;

    /// <summary>The N-tier ladder. <paramref name="runnerIndex"/> names the reward-to-risk tier (the gated draw); any
    /// tiers beyond it (e.g. the TGR-1/2 SD projections) are deeper advisory targets that do NOT move the RR.</summary>
    public TargetLadder(Direction direction, IReadOnlyList<Price> targets, int runnerIndex)
    {
        ArgumentNullException.ThrowIfNull(targets);
        Guard.Against(targets.Count < 1, "A target ladder must have at least one tier.");
        Guard.Against(runnerIndex < 0 || runnerIndex >= targets.Count, "The runner index must name a valid tier.");
        for (var i = 1; i < targets.Count; i++)
        {
            var ordered = direction == Direction.Bullish
                ? targets[i].Value > targets[i - 1].Value
                : targets[i].Value < targets[i - 1].Value;
            Guard.Against(!ordered, "Target ladder tiers must be strictly ordered in the trade direction.");
        }

        Direction = direction;
        _targets = targets;
        RunnerIndex = runnerIndex;
    }

    /// <summary>The legacy two-tier ladder — kept so existing call sites need no churn; the runner is the second tier.</summary>
    public TargetLadder(Direction direction, Price partial, Price runner)
        : this(direction, new[] { partial, runner }, CanonicalRunnerIndex)
    {
    }

    public Direction Direction { get; }

    /// <summary>The index of the reward-to-risk runner tier (the gated draw) within <see cref="Targets"/>.</summary>
    public int RunnerIndex { get; }

    /// <summary>T1 — the partial-profit target (the shallowest tier).</summary>
    public Price Partial => _targets[0];

    /// <summary>The runner target the reward-to-risk is measured to (the gated draw — NOT necessarily the deepest tier).</summary>
    public Price Runner => _targets[RunnerIndex];

    /// <summary>The full ordered ladder T1..Tn (the runner plus any deeper SD-projection extensions).</summary>
    public IReadOnlyList<Price> Targets => _targets;

    public int TierCount => _targets.Count;
}

/// <summary>
/// A self-validating, ADVISORY priced trade plan (plan §2.5.1 steps 7–9, §3.0): direction, entry (the OTE array
/// level), stop (beyond the swept extreme), the tiered targets, and the reward-to-risk measured entry→runner.
/// The total price order is enforced — bullish <c>stop &lt; entry &lt; T1 &lt; … &lt; Tn</c>, bearish the mirror — so a
/// swapped stop/target can never form a plan. It prices nothing for execution; the paper-trade simulator consumes it.
/// </summary>
public readonly record struct TradePlan
{
    public TradePlan(Direction direction, Price entry, Price stop, TargetLadder targets)
    {
        // The ladder's own ctor guarantees T1 < … < Tn in the direction; here we only need stop -> entry -> T1.
        var orderedLong = direction == Direction.Bullish
            && stop.Value < entry.Value
            && entry.Value < targets.Partial.Value;
        var orderedShort = direction == Direction.Bearish
            && stop.Value > entry.Value
            && entry.Value > targets.Partial.Value;
        Guard.Against(
            !(orderedLong || orderedShort),
            "TradePlan prices must run stop -> entry -> T1 -> … -> Tn strictly in the trade direction.");

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

    /// <summary>Reward-to-risk measured entry→runner (the gated-draw tier, NOT necessarily the deepest target) — the
    /// §2.5 RR gate, recomputed from the geometry.</summary>
    public RewardRatio RewardRatio { get; }
}
