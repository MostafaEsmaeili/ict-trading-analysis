using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.Trading;
using IctTrader.PaperTrading.Application;
using IctTrader.PaperTrading.Application.Trading;
using IctTrader.PaperTrading.Contracts;
using Microsoft.Extensions.Options;

namespace IctTrader.UnitTests.PaperTrading;

/// <summary>
/// The account-status read-side must be a PURE read: before any trade has run the demo account row does not exist,
/// and the handler must synthesize a clean opening snapshot from the validated config rather than create the row in
/// a GET. This locks that no-account path so the live-config panel can be polled freely without side effects.
/// </summary>
public sealed class GetAccountStatusQueryHandlerTests
{
    [Fact]
    public async Task Synthesizes_a_clean_opening_snapshot_from_config_when_no_account_exists_yet()
    {
        var risk = new RiskOptions();
        var handler = new GetAccountStatusQueryHandler(
            new NullAccountRepository(),
            new EmptyTradeRepository(),
            Options.Create(new PaperTradingOptions { StartingEquity = 25_000m }),
            Options.Create(risk));

        var status = await handler.HandleAsync(new GetAccountStatusQuery());

        status.StartingEquity.Should().Be(25_000m);
        status.Equity.Should().Be(25_000m);
        status.PeakEquity.Should().Be(25_000m);
        status.DrawdownTrough.Should().Be(25_000m);
        status.OpenRisk.Should().Be(0m);
        status.OpenRiskCap.Should().Be(25_000m * risk.MaxOpenPortfolioRiskPercent / 100m);
        status.RiskUtilizationPercent.Should().Be(0m);
        status.MaxOpenPortfolioRiskPercent.Should().Be(risk.MaxOpenPortfolioRiskPercent);
        status.ConsecutiveWins.Should().Be(0);
        status.ConsecutiveLosses.Should().Be(0);
        status.OpenTradeCount.Should().Be(0);
    }

    private sealed class NullAccountRepository : IPaperAccountRepository
    {
        public Task<PaperAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<PaperAccount?>(null);

        public Task AddAsync(PaperAccount account, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class EmptyTradeRepository : IPaperTradeRepository
    {
        public Task<PaperTrade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<PaperTrade?>(null);

        public Task AddAsync(PaperTrade trade, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<PaperTrade>> GetOpenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PaperTrade>>([]);

        public Task<IReadOnlyList<PaperTrade>> GetClosedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PaperTrade>>([]);

        public Task<IReadOnlyList<PaperTrade>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PaperTrade>>([]);
    }
}
