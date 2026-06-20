using IctTrader.Domain.Common;
using IctTrader.Domain.Detection;

namespace IctTrader.Domain.Setups;

/// <summary>
/// The human-readable reasoning for a setup (plan §4.5): the per-confluence clauses in the §2.5 narrative order
/// (the FSM already rank-sorts them), closed by a priced summary clause. Built from the centralised
/// <see cref="ReasonFragments"/> templates so it carries no inline literals; the prices come solely from the
/// <see cref="TradePlan"/> so the sentence and the chart lines cannot disagree.
/// </summary>
public readonly record struct SetupReason
{
    public SetupReason(string text)
    {
        Guard.Against(string.IsNullOrWhiteSpace(text), "SetupReason text must not be empty.");
        Text = text;
    }

    public string Text { get; }

    public override string ToString() => Text;

    public static SetupReason Compose(IReadOnlyList<ConfluenceContribution> confluences, TradePlan plan)
    {
        ArgumentNullException.ThrowIfNull(confluences);

        var clauses = confluences
            .Select(c => c.ReasonFragment)
            .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
            .Append(ReasonFragments.TradePlanSummary(
                plan.Direction,
                plan.Entry.Value,
                plan.Stop.Value,
                plan.Targets.Partial.Value,
                plan.Targets.Runner.Value,
                plan.RewardRatio.Value));

        return new SetupReason(string.Join("; ", clauses));
    }
}
