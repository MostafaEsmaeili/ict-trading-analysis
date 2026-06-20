using IctTrader.Domain.Sessions;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// The operator's killzone hunt-set (plan §4.6) — the §2.5.2 <c>KillzoneEntry</c> RequiredCondition only matches
/// inside an ENABLED killzone, so the operator can choose to trade only London Open, only New York, etc. The
/// enabled set is constrained to the FROZEN selectable subset (shared with <see cref="MarketContextOptions"/>);
/// the index morning is governed by instrument class, not this set. Bound from <c>Ict:Detection:Killzone</c>.
/// </summary>
public sealed class KillzoneEntryOptions
{
    public const string SectionName = "Ict:Detection:Killzone";

    /// <summary>The enabled killzones — default is ICT's primary preference (London Open + New York AM).</summary>
    public IReadOnlyList<Killzone> ActiveKillzones { get; init; } =
        [Killzone.LondonOpen, Killzone.NewYorkOpen];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (ActiveKillzones is null || ActiveKillzones.Count == 0)
        {
            errors.Add("At least one active killzone must be configured.");
            return errors;
        }

        foreach (var killzone in ActiveKillzones.Where(k => !MarketContextOptions.SelectableKillzones.Contains(k)))
        {
            errors.Add($"ActiveKillzones must be a subset of [{string.Join(", ", MarketContextOptions.SelectableKillzones)}] but contained {killzone}.");
        }

        return errors;
    }
}
