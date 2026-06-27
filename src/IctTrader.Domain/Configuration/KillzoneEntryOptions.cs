using IctTrader.Domain.Sessions;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// The operator's killzone hunt-set (plan §4.6) — the §2.5.2 <c>KillzoneEntry</c> RequiredCondition only matches
/// inside an ENABLED killzone, so the operator can choose to trade only London Open, only New York, etc. The
/// enabled set is constrained to the FROZEN selectable subset (shared with <see cref="MarketContextOptions"/>);
/// the index morning is governed by instrument class, not this set. Bound from <c>Ict:Scanning</c> — this is the
/// SINGLE source of the killzone hunt-set, so the operator's <c>Ict:Scanning:ActiveKillzones</c> actually drives
/// the <see cref="IctTrader.Domain.Detection.Detectors.KillzoneEntryDetector"/> and the no-chase entry rung
/// (it previously read a separate, never-wired <c>Ict:Detection:Killzone</c> section).
/// </summary>
public sealed class KillzoneEntryOptions
{
    public const string SectionName = MarketContextOptions.SectionName;

    // The operator-selected list defaults to EMPTY, not the business default. This is load-bearing: the .NET
    // configuration binder APPENDS bound array items to a pre-populated collection initializer rather than
    // replacing it (see MarketContextOptions.cs for the documented rationale), so a non-empty default would be
    // silently prepended to the operator's config — e.g. `["LondonOpen"]` bound onto a `[LondonOpen, NewYorkOpen]`
    // default would still hunt NewYorkOpen. With an empty default the binder replaces cleanly; the ICT business
    // default is applied by ResolvedActiveKillzones (which the consumers read), and an unconfigured list falls
    // back to that default there.
    public IReadOnlyList<Killzone> ActiveKillzones { get; init; } = [];

    private static readonly IReadOnlyList<Killzone> DefaultActiveKillzones =
        [Killzone.LondonOpen, Killzone.NewYorkOpen];

    /// <summary>
    /// The killzones the entry gate hunts — the configured set de-duplicated, or the ICT default (London Open +
    /// New York AM) when none is configured. Consume this, never the raw <see cref="ActiveKillzones"/>.
    /// </summary>
    public IReadOnlyList<Killzone> ResolvedActiveKillzones =>
        ActiveKillzones.Count == 0 ? DefaultActiveKillzones : ActiveKillzones.Distinct().ToArray();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        // An empty configured list is VALID — it means "use the ICT default" (applied by ResolvedActiveKillzones).
        // We validate only the CONFIGURED members so a typo'd killzone still fails fast.
        foreach (var killzone in ActiveKillzones.Where(k => !MarketContextOptions.SelectableKillzones.Contains(k)))
        {
            errors.Add($"ActiveKillzones must be a subset of [{string.Join(", ", MarketContextOptions.SelectableKillzones)}] but contained {killzone}.");
        }

        return errors;
    }
}
