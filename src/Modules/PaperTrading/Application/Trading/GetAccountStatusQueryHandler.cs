using IctTrader.Domain.Configuration;
using IctTrader.Domain.Repositories;
using IctTrader.PaperTrading.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.Options;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// The PaperTrading module's live account-status read-side (plan §5.1/§5.3): it answers
/// <see cref="GetAccountStatusQuery"/> so the dashboard's live-config panel can show the demo account's equity vs
/// its configured starting equity, the adaptive-risk peak/trough + win/loss streaks (§2.4/§2.5.5), and the current
/// open risk against the §2.5.10 portfolio cap. It reads the single demo account by its well-known id and the open
/// trade count from the trade repository.
///
/// <para>Pure read — it NEVER creates the account (so the status endpoint can't write): before any trade has run the
/// account row does not exist, and the handler synthesizes a clean opening snapshot from the validated config
/// instead. Routes nowhere near an order path (§6.3).</para>
/// </summary>
public sealed class GetAccountStatusQueryHandler(
    IPaperAccountRepository accounts,
    IPaperTradeRepository trades,
    IOptions<PaperTradingOptions> paperTrading,
    IOptions<RiskOptions> risk)
    : IQueryHandler<GetAccountStatusQuery, AccountStatusDto>
{
    private readonly IPaperAccountRepository _accounts =
        accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly IPaperTradeRepository _trades = trades ?? throw new ArgumentNullException(nameof(trades));
    private readonly PaperTradingOptions _paperTrading =
        (paperTrading ?? throw new ArgumentNullException(nameof(paperTrading))).Value;
    private readonly RiskOptions _risk = (risk ?? throw new ArgumentNullException(nameof(risk))).Value;

    public async Task<AccountStatusDto> HandleAsync(
        GetAccountStatusQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var startingEquity = _paperTrading.StartingEquity;
        var account = await _accounts
            .GetByIdAsync(PaperAccountProvider.DemoAccountId, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            // No trade has run yet — emit a clean opening snapshot from config; never create the row in a read path.
            var cap = _risk.MaxOpenPortfolioRiskPercent;
            return new AccountStatusDto(
                StartingEquity: startingEquity,
                Equity: startingEquity,
                PeakEquity: startingEquity,
                DrawdownTrough: startingEquity,
                OpenRisk: 0m,
                OpenRiskCap: startingEquity * cap / 100m,
                RiskUtilizationPercent: 0m,
                MaxOpenPortfolioRiskPercent: cap,
                ConsecutiveWins: 0,
                ConsecutiveLosses: 0,
                OpenTradeCount: 0);
        }

        var openCount = (await _trades.GetOpenAsync(cancellationToken).ConfigureAwait(false)).Count;
        var state = account.RiskState;
        var openRisk = account.OpenRisk.Amount;
        var openRiskCap = account.OpenRiskCap.Amount;

        return new AccountStatusDto(
            StartingEquity: startingEquity,
            Equity: account.Equity.Amount,
            PeakEquity: state.PeakEquity.Amount,
            DrawdownTrough: state.DipTrough.Amount,
            OpenRisk: openRisk,
            OpenRiskCap: openRiskCap,
            RiskUtilizationPercent: openRiskCap <= 0m ? 0m : openRisk / openRiskCap * 100m,
            MaxOpenPortfolioRiskPercent: account.MaxOpenPortfolioRiskPercent,
            ConsecutiveWins: state.ConsecutiveWins,
            ConsecutiveLosses: state.ConsecutiveLosses,
            OpenTradeCount: openCount);
    }
}
