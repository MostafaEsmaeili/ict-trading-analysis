using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Styles;

/// <summary>
/// Pure domain service for trade-style selection (plan §4.7): resolves the timeframe policy for a chosen
/// style from config, and classifies a detected setup's style from its expected hold (inclusive hold-band
/// upper edges). All numbers come from <see cref="TradeStyleOptions"/>.
/// </summary>
public sealed class TradeStyleClassifier
{
    private readonly TradeStyleOptions _options;

    public TradeStyleClassifier(TradeStyleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Resolves the immutable Bias/Structure/Entry timeframe policy for a chosen style.</summary>
    public TimeframePolicy ResolvePolicy(TradeStyle style)
    {
        var settings = _options.For(style);
        return new TimeframePolicy(
            style,
            settings.BiasTimeframe,
            settings.StructureTimeframe,
            settings.EntryTimeframe,
            TimeSpan.FromMinutes(settings.MaxHoldMinutes),
            new RewardRatio(settings.MinRewardRatio));
    }

    /// <summary>
    /// Classifies a setup's style from its expected hold using the configured per-style hold caps as
    /// inclusive upper band edges (≤ Scalp cap ⇒ Scalp, ≤ Intraday cap ⇒ Intraday, ≤ Swing cap ⇒ Swing,
    /// else Position).
    /// </summary>
    public TradeStyle ClassifyByHold(TimeSpan expectedHold)
    {
        Guard.Against(expectedHold <= TimeSpan.Zero, "Expected hold must be positive.");
        var minutes = expectedHold.TotalMinutes;

        if (minutes <= _options.Scalp.MaxHoldMinutes)
        {
            return TradeStyle.Scalp;
        }

        if (minutes <= _options.Intraday.MaxHoldMinutes)
        {
            return TradeStyle.Intraday;
        }

        return minutes <= _options.Swing.MaxHoldMinutes ? TradeStyle.Swing : TradeStyle.Position;
    }

    /// <summary>Whether the style may take a direct-FVG (Silver-Bullet) entry and skip the OTE retrace.</summary>
    public bool AllowsDirectFvgEntry(TradeStyle style) => _options.For(style).AllowDirectFvgEntry;
}
