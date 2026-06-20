using IctTrader.Domain.Detection;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Setups;

/// <summary>
/// The priced trade frame the <see cref="ConfluenceCondition.DrawTargetRrMet"/> detector established and that
/// passed the reward-to-risk floor (plan §2.5.1 steps 2/8/9): the entry (OTE array level), the stop (beyond the
/// swept extreme), the draw target (T2), and the reward-to-risk. The confluence FSM captures it straight from
/// that detector's evidence so the <see cref="SetupFactory"/> prices the setup against the EXACT draw that was
/// gated — it never re-derives the target (a pool registered/swept later could differ).
/// </summary>
public readonly record struct PricedFrame(Direction Direction, decimal Entry, decimal Stop, decimal Target, decimal RewardRatio)
{
    /// <summary>Reconstructs the frame from a draw-on-liquidity detector result's evidence, or null if incomplete.</summary>
    public static PricedFrame? TryFromEvidence(Direction direction, IReadOnlyDictionary<string, object>? evidence)
    {
        if (evidence is not null
            && evidence.TryGetValue(EvidenceKeys.EntryPrice, out var entry) && entry is decimal entryPrice
            && evidence.TryGetValue(EvidenceKeys.StopPrice, out var stop) && stop is decimal stopPrice
            && evidence.TryGetValue(EvidenceKeys.TargetPrice, out var target) && target is decimal targetPrice
            && evidence.TryGetValue(EvidenceKeys.RewardRatio, out var rr) && rr is decimal rewardRatio)
        {
            return new PricedFrame(direction, entryPrice, stopPrice, targetPrice, rewardRatio);
        }

        return null;
    }
}
