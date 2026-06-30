using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;

namespace IctTrader.PaperTrading.Application;

/// <summary>
/// The PaperTrading module's account-bootstrap policy (bound from <c>Ict:PaperTrading</c>) — no magic numbers. The
/// single demo <see cref="IctTrader.Domain.Trading.PaperAccount"/> the module trades is loaded-or-created from these
/// values: the starting equity and the aggregate open-risk cap (the §2.5.10 portfolio cap, reused from
/// <see cref="IctTrader.Domain.Configuration.RiskOptions.MaxOpenPortfolioRiskPercent"/> so the per-trade and
/// aggregate caps stay consistent). Validated at startup via <c>ValidateOnStart</c>.
/// </summary>
public sealed class PaperTradingOptions
{
    public const string SectionName = "Ict:PaperTrading";

    /// <summary>The demo account's opening equity in account currency (§5.1 default 10,000).</summary>
    public decimal StartingEquity { get; init; } = 10_000m;

    /// <summary>
    /// The GLOBAL Auto-vs-Manual TAKE workflow default (plan §15 — the operator's "give me the opportunity to use that
    /// setup"). The POCO code default is <see cref="TradeEntryMode.Auto"/> SO every code-constructed-options unit/
    /// integration test stays BYTE-IDENTICAL (no mass churn); the running PRODUCT sets
    /// <c>Ict:PaperTrading:DefaultEntryMode = "Manual"</c> in <c>appsettings.json</c> so the live default IS the Take
    /// workflow. A per-instrument override (<see cref="InstrumentOptionOverrides.EntryMode"/>, live-editable) wins over
    /// this. Paper-only either way — both modes end at the SAME simulated open (§6.3).
    /// </summary>
    public TradeEntryMode DefaultEntryMode { get; init; } = TradeEntryMode.Auto;

    /// <summary>
    /// The EFFECTIVE TAKE workflow for a symbol = its per-instrument override (if set) else the global
    /// <see cref="DefaultEntryMode"/>. The caller supplies the symbol's resolved overrides (from the
    /// <see cref="IInstrumentRegistry"/>, which already overlays the live runtime settings), so this method stays a
    /// pure null-coalesce with no I/O or ambient state. A <c>null</c> bundle (no override) resolves to the global default.
    /// </summary>
    public TradeEntryMode EffectiveEntryMode(InstrumentOptionOverrides? overrides) =>
        overrides?.EntryMode ?? DefaultEntryMode;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (StartingEquity <= 0m)
        {
            errors.Add($"StartingEquity must be positive but was {StartingEquity}.");
        }

        if (!Enum.IsDefined(DefaultEntryMode))
        {
            errors.Add($"DefaultEntryMode must be a defined {nameof(TradeEntryMode)} value but was {(int)DefaultEntryMode}.");
        }

        return errors;
    }
}
