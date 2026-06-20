using IctTrader.Domain.Sessions;
using IctTrader.Domain.Styles;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable per-symbol scanning state (plan §4.1/§4.6). The ring-buffer depth, the per-type open-array cap,
/// the midnight session reset, and the operator-selected active killzones/styles are all bound from
/// <c>Ict:Scanning</c> — nothing here is a literal. The Host validates this via <see cref="Validate"/>.
/// </summary>
public sealed class MarketContextOptions
{
    public const string SectionName = "Ict:Scanning";

    public int WindowCapacity { get; init; } = 512;

    public int MaxOpenArraysPerType { get; init; } = 64;

    public bool ResetSessionStateAtNyMidnight { get; init; } = true;

    public IReadOnlyList<Killzone> ActiveKillzones { get; init; } =
        [Killzone.LondonOpen, Killzone.NewYorkOpen];

    public IReadOnlyList<TradeStyle> ActiveStyles { get; init; } = [TradeStyle.Intraday];

    /// <summary>
    /// The killzones an operator may enable via <c>Ict:Scanning:ActiveKillzones</c> — the FROZEN CONTRACT
    /// subset (plan §11.1). <see cref="Killzone.Pm"/>/<see cref="Killzone.Am"/> are internal classification
    /// outcomes (FX afternoon / index morning) governed by instrument class, not operator-selectable here;
    /// <see cref="Killzone.None"/> is not a killzone.
    /// </summary>
    public static IReadOnlyList<Killzone> SelectableKillzones { get; } =
        [Killzone.Asian, Killzone.LondonOpen, Killzone.NewYorkOpen, Killzone.LondonClose];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (WindowCapacity < 3)
        {
            errors.Add($"WindowCapacity must be at least 3 (for 3-candle patterns) but was {WindowCapacity}.");
        }

        if (MaxOpenArraysPerType < 1)
        {
            errors.Add($"MaxOpenArraysPerType must be at least 1 but was {MaxOpenArraysPerType}.");
        }

        if (ActiveKillzones is null || ActiveKillzones.Count == 0)
        {
            errors.Add("At least one active killzone must be configured.");
        }
        else
        {
            foreach (var killzone in ActiveKillzones.Where(k => !SelectableKillzones.Contains(k)))
            {
                errors.Add($"ActiveKillzones must be a subset of [{string.Join(", ", SelectableKillzones)}] but contained {killzone}.");
            }
        }

        if (ActiveStyles is null || ActiveStyles.Count == 0)
        {
            errors.Add("At least one active trade style must be configured.");
        }

        return errors;
    }
}
