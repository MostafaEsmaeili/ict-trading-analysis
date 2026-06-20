using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// Builds one sized <see cref="PaperTrade"/> from a confirmed advisory <see cref="Setup"/> and a
/// <see cref="PaperAccount"/> (plan §3.0 factory / §5.1). It sizes the position from the configured base risk,
/// confirms the account can reserve that risk within the portfolio cap, opens the trade at the plan's prices,
/// and reserves the risk on the account — a single atomic step. The trade inherits the Setup's bias-aligned
/// direction, so a counter-bias trade is structurally impossible (a counter-bias setup never becomes a Setup).
/// This slice uses the flat <see cref="RiskOptions.BaseRiskPercent"/>; the adaptive loss-ladder is a fast-follow.
/// </summary>
public sealed class PaperTradeFactory
{
    private readonly RiskOptions _risk;

    public PaperTradeFactory(RiskOptions risk)
    {
        ArgumentNullException.ThrowIfNull(risk);
        _risk = risk;
    }

    public PaperTrade Open(
        Setup setup,
        PaperAccount account,
        SymbolSpec symbolSpec,
        ContractSpec contractSpec,
        DateTimeOffset openedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(symbolSpec);
        ArgumentNullException.ThrowIfNull(contractSpec);

        var sizing = PositionSizer.Size(
            account.Equity,
            new RiskPercent(_risk.BaseRiskPercent),
            setup.Plan,
            symbolSpec,
            contractSpec,
            new Pips(_risk.MinStopDistancePips));

        var trade = new PaperTrade(
            Guid.NewGuid(),
            account.Id,
            setup.Symbol,
            setup.Style,
            setup.Timeframe,
            setup.Plan,
            sizing.Size,
            symbolSpec.PipSize,
            contractSpec.ValuePerPip,
            openedAtUtc);

        // The account is the authoritative cap gate: it throws (without mutating) if the trade would breach the
        // portfolio open-risk cap, so the open is atomic — a refused trade leaves the account untouched.
        account.RegisterOpen(trade);
        return trade;
    }
}
