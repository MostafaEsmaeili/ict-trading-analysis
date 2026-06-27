namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable gate for the OPTIONAL <c>OpenPriceReference</c> confluence (plan §2.5.2 step 4 / §2.5.8). It carries no
/// numeric thresholds — the open-price relationship is read structurally against the existing
/// <c>MarketContext.ReferenceOpen</c> (midnight / 08:30 macro). The only knob is an enable flag (default ON: it is an
/// additive, scoring-only confluence — never a hard gate — so its absence never blocks a setup, it only fails to
/// promote one toward Grade A). Bound from <c>Ict:Detection:OpenPriceReference</c>.
/// </summary>
public sealed class OpenPriceReferenceOptions
{
    public const string SectionName = "Ict:Detection:OpenPriceReference";

    /// <summary>Whether the open-price-reference confluence is scored. Default ON — additive scoring only.</summary>
    public bool Enabled { get; init; } = true;

    public IReadOnlyList<string> Validate() => [];
}
