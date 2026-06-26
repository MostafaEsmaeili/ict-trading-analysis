using IctTrader.Domain.Configuration;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// Loads-or-creates the single demo <see cref="PaperAccount"/> the module trades against (plan §5.1). The account
/// has a fixed well-known id (<see cref="DemoAccountId"/>) so both bus handlers — running in separate DI scopes —
/// resolve the SAME account row and reserve/settle against one consistent ledger. On first use the account does
/// not exist, so it is constructed from the validated config (starting equity from <see cref="PaperTradingOptions"/>,
/// the portfolio cap from <see cref="RiskOptions.MaxOpenPortfolioRiskPercent"/> — reused so the per-trade and
/// aggregate caps stay consistent) and staged via <see cref="IPaperAccountRepository.AddAsync"/>; the caller commits
/// it with the rest of the unit of work.
///
/// <para>It is a SCOPED service (it depends on the scoped repository), so the returned instance is tracked by the
/// current scope's context — the DB-as-state design: the account state lives in the database, never in a singleton
/// cached across dispatches (which would detach from the next scope's context).</para>
/// </summary>
public sealed class PaperAccountProvider : IPaperAccountProvider
{
    /// <summary>The fixed identity of the single demo paper account (plan §5.1). A well-known constant so every
    /// dispatch resolves the same account from the database.</summary>
    public static readonly Guid DemoAccountId = new("d3f0acc0-0000-0000-0000-000000000001");

    private readonly IPaperAccountRepository _accounts;
    private readonly PaperTradingOptions _paperTrading;
    private readonly RiskOptions _risk;

    public PaperAccountProvider(
        IPaperAccountRepository accounts,
        IOptions<PaperTradingOptions> paperTrading,
        IOptions<RiskOptions> risk)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _paperTrading = paperTrading.Value;
        _risk = risk.Value;
    }

    public async Task<PaperAccount> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _accounts.GetByIdAsync(DemoAccountId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var account = new PaperAccount(
            DemoAccountId,
            new Money(_paperTrading.StartingEquity),
            _risk.MaxOpenPortfolioRiskPercent);

        await _accounts.AddAsync(account, cancellationToken).ConfigureAwait(false);
        return account;
    }
}
