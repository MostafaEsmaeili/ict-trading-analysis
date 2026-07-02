using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Setups;

/// <summary>
/// Builds the priced <see cref="Setup"/> aggregate from a confirmed <see cref="SetupConfirmation"/> (plan §4.5).
/// It prices the plan against the EXACT draw frame the FSM captured (it NEVER re-derives the target — a pool
/// registered or swept after confirmation could differ), places T1 at the configured equilibrium of the
/// entry→T2 leg, and re-checks the reward-to-risk floor of the chosen style (clamped by the hard 2:1) as a
/// belt-and-suspenders invariant over the upstream gate. A pure domain service.
/// </summary>
public sealed class SetupFactory
{
    private readonly TargetLadderOptions _ladder;
    private readonly TradeStyleOptions _styles;

    public SetupFactory(TargetLadderOptions ladder, TradeStyleOptions styles)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(styles);
        _ladder = ladder;
        _styles = styles;
    }

    /// <summary>Prices and constructs the advisory Setup for the given style (stamped with the confirming
    /// <paramref name="model"/>, plan §16), or throws if the frame is missing/invalid.</summary>
    public Setup Create(SetupConfirmation confirmation, TradeStyle style, SetupModel model = SetupModel.Ict2022)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        if (confirmation.Frame is not { } frame)
        {
            throw new InvalidOperationException("Cannot price a Setup: the confirmation carries no draw-target frame.");
        }

        var partial = frame.Entry + (_ladder.T1EquilibriumFraction * (frame.Target - frame.Entry));

        // T1 (equilibrium partial) + T2 (the gated range draw) + any SD projection tiers STRICTLY beyond T2 (TGR-1/2).
        // The SD tiers arrive ordered shallow→deep, so the ladder stays a clean monotone {T1, T2, deeper SD tiers};
        // when no SD targets ride the frame the ladder is the byte-identical two tiers.
        var tiers = new List<Price> { new(partial), new(frame.Target) };
        foreach (var sd in frame.SdTargets)
        {
            var beyondTarget = frame.Direction == Direction.Bullish ? sd > frame.Target : sd < frame.Target;
            if (beyondTarget)
            {
                tiers.Add(new Price(sd));
            }
        }

        // The gated range draw (the canonical runner tier) is the reward-to-risk runner; the SD tiers are deeper
        // advisory targets, so enabling SD does NOT change the RR the FSM gated.
        var plan = new TradePlan(
            frame.Direction,
            new Price(frame.Entry),
            new Price(frame.Stop),
            new TargetLadder(frame.Direction, tiers, TargetLadder.CanonicalRunnerIndex));

        var floor = Math.Max(_styles.For(style).MinRewardRatio, _styles.AbsoluteMinRewardRatio);
        Guard.Against(
            plan.RewardRatio.Value < floor,
            $"Setup reward-to-risk {plan.RewardRatio} is below the {floor} floor for {style}.");

        var reason = SetupReason.Compose(confirmation.Confluences, plan);
        return new Setup(
            confirmation.Symbol,
            style,
            confirmation.Timeframe,
            confirmation.Grade,
            confirmation.Score,
            plan,
            reason,
            confirmation.ConfirmedAtUtc,
            frame.StackedFartherBound, // FVG-SEM-2b: carried for the wrong-order NIX, never a TradePlan tier
            model);
    }
}
