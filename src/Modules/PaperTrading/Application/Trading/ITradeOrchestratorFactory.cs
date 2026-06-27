using IctTrader.Domain.Configuration;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>Creates a fresh stateless <see cref="TradeOrchestrator"/> for one symbol. The orchestrator is
/// per-symbol because its <see cref="EntryFillEvaluator"/> binds a per-symbol <see cref="SymbolSpec"/>; the
/// registry calls this on a cache miss. Production wiring resolves it as a singleton (the resolved options are
/// immutable).</summary>
public interface ITradeOrchestratorFactory
{
    /// <summary>
    /// Builds the orchestrator for <paramref name="symbol"/>. <paramref name="risk"/> overrides the host's
    /// configured <see cref="RiskOptions"/> for this orchestrator (the on-demand backtest passes a per-run risk
    /// policy so an operator can size the run); <c>null</c> uses the host default. The per-instrument overrides
    /// still apply on top of whichever risk policy is used.
    /// </summary>
    TradeOrchestrator Create(Symbol symbol, RiskOptions? risk = null);
}
