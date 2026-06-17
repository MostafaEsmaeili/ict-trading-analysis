using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Styles;

/// <summary>
/// Trade style selecting which slice of the ICT top-down cascade the scanner uses (plan §4.7). FROZEN
/// CONTRACT (plan §11.1 #7): member names back the dashboard style filter and the chart's style badge.
/// <see cref="Intraday"/> is the default — the §2.5 entry model.
/// </summary>
public enum TradeStyle
{
    Scalp,
    Intraday,
    Swing,
    Position,
}

/// <summary>
/// The Bias/Structure/Entry timeframe triple plus the hold cap and minimum reward ratio for a
/// <see cref="TradeStyle"/> (plan §4.7). FROZEN CONTRACT shape (plan §11.1 #7). Concrete per-style
/// defaults are config-bound (<c>Ict:TradeStyles</c>) and resolved/classified by the
/// TradeStyleClassifier domain service in WP1 — this value object only carries a resolved policy.
/// </summary>
public sealed record TimeframePolicy
{
    public TimeframePolicy(
        TradeStyle style,
        Timeframe biasTimeframe,
        Timeframe structureTimeframe,
        Timeframe entryTimeframe,
        TimeSpan maxHold,
        RewardRatio minRewardRatio)
    {
        Guard.Against(maxHold <= TimeSpan.Zero, "TimeframePolicy.MaxHold must be positive.");
        Style = style;
        BiasTimeframe = biasTimeframe;
        StructureTimeframe = structureTimeframe;
        EntryTimeframe = entryTimeframe;
        MaxHold = maxHold;
        MinRewardRatio = minRewardRatio;
    }

    public TradeStyle Style { get; }

    public Timeframe BiasTimeframe { get; }

    public Timeframe StructureTimeframe { get; }

    public Timeframe EntryTimeframe { get; }

    public TimeSpan MaxHold { get; }

    public RewardRatio MinRewardRatio { get; }
}
