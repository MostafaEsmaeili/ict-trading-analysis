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

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (StartingEquity <= 0m)
        {
            errors.Add($"StartingEquity must be positive but was {StartingEquity}.");
        }

        return errors;
    }
}
