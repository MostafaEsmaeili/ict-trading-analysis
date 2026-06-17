using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Per-style configuration (plan §4.7) — the Bias/Structure/Entry timeframe triple, hold cap, RR floor,
/// overnight policy, and the Scalp Silver-Bullet toggle. Bound from <c>Ict:TradeStyles</c>. The hold caps for
/// Swing/Position are operator-tunable engineering defaults (the transcripts say only "days/weeks").
/// </summary>
public sealed class TradeStyleOptions
{
    public const string SectionName = "Ict:TradeStyles";

    public StyleSettings Scalp { get; init; } = StyleSettings.DefaultScalp;

    public StyleSettings Intraday { get; init; } = StyleSettings.DefaultIntraday;

    public StyleSettings Swing { get; init; } = StyleSettings.DefaultSwing;

    public StyleSettings Position { get; init; } = StyleSettings.DefaultPosition;

    /// <summary>The hard 2:1 RR floor (§2.5.10 resolution 2 — never silently raised to 3).</summary>
    public decimal AbsoluteMinRewardRatio { get; init; } = 2.0m;

    /// <summary>No-overnight styles must not exceed this hold cap (intraday-class guard).</summary>
    public int IntradayClassMaxHoldGuardMinutes { get; init; } = 1440;

    public StyleSettings For(TradeStyle style) => style switch
    {
        TradeStyle.Scalp => Scalp,
        TradeStyle.Intraday => Intraday,
        TradeStyle.Swing => Swing,
        TradeStyle.Position => Position,
        _ => Intraday,
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        foreach (var style in Enum.GetValues<TradeStyle>())
        {
            var settings = For(style);

            if (!((int)settings.BiasTimeframe > (int)settings.StructureTimeframe
                  && (int)settings.StructureTimeframe > (int)settings.EntryTimeframe))
            {
                errors.Add($"{style}: timeframes must descend strictly Bias > Structure > Entry " +
                    $"({settings.BiasTimeframe} > {settings.StructureTimeframe} > {settings.EntryTimeframe}).");
            }

            if (settings.MaxHoldMinutes <= 0)
            {
                errors.Add($"{style}: MaxHoldMinutes must be positive but was {settings.MaxHoldMinutes}.");
            }

            if (settings.MinRewardRatio < AbsoluteMinRewardRatio)
            {
                errors.Add($"{style}: MinRewardRatio {settings.MinRewardRatio} is below the {AbsoluteMinRewardRatio} floor.");
            }

            if (!settings.AllowOvernight && settings.MaxHoldMinutes > IntradayClassMaxHoldGuardMinutes)
            {
                errors.Add($"{style}: a no-overnight style cannot hold longer than {IntradayClassMaxHoldGuardMinutes} minutes.");
            }
        }

        return errors;
    }
}

/// <summary>The resolved settings for one <see cref="TradeStyle"/> (plan §4.7).</summary>
public sealed class StyleSettings
{
    public required Timeframe BiasTimeframe { get; init; }

    public required Timeframe StructureTimeframe { get; init; }

    public required Timeframe EntryTimeframe { get; init; }

    public required int MaxHoldMinutes { get; init; }

    public required bool AllowOvernight { get; init; }

    public required decimal MinRewardRatio { get; init; }

    /// <summary>
    /// Scalp Silver-Bullet direct-FVG entry (skip the OTE retrace). Primer-sourced/provenance-flagged, so it
    /// defaults FALSE to preserve the §2.5 OTE RequiredCondition; an operator may opt in.
    /// </summary>
    public bool AllowDirectFvgEntry { get; init; }

    public static StyleSettings DefaultScalp => new()
    {
        BiasTimeframe = Timeframe.H1,
        StructureTimeframe = Timeframe.M5,
        EntryTimeframe = Timeframe.M1,
        MaxHoldMinutes = 30,
        AllowOvernight = false,
        MinRewardRatio = 2.5m,
        AllowDirectFvgEntry = false,
    };

    public static StyleSettings DefaultIntraday => new()
    {
        BiasTimeframe = Timeframe.D1,
        StructureTimeframe = Timeframe.M15,
        EntryTimeframe = Timeframe.M5,
        MaxHoldMinutes = 120,
        AllowOvernight = false,
        MinRewardRatio = 2.5m,
        AllowDirectFvgEntry = false,
    };

    public static StyleSettings DefaultSwing => new()
    {
        BiasTimeframe = Timeframe.W1,
        StructureTimeframe = Timeframe.H4,
        EntryTimeframe = Timeframe.M15,
        MaxHoldMinutes = 14400, // ~10 days; NOT transcript-stated, operator-tunable
        AllowOvernight = true,
        MinRewardRatio = 2.5m,
        AllowDirectFvgEntry = false,
    };

    public static StyleSettings DefaultPosition => new()
    {
        BiasTimeframe = Timeframe.MN1,
        StructureTimeframe = Timeframe.D1,
        EntryTimeframe = Timeframe.H4,
        MaxHoldMinutes = 43200, // ~30 days; NOT transcript-stated, operator-tunable
        AllowOvernight = true,
        MinRewardRatio = 2.5m,
        AllowDirectFvgEntry = false,
    };
}
