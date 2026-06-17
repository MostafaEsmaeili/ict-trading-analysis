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

        if (ActiveKillzones.Count == 0)
        {
            errors.Add("At least one active killzone must be configured.");
        }

        if (ActiveStyles.Count == 0)
        {
            errors.Add("At least one active trade style must be configured.");
        }

        return errors;
    }
}
