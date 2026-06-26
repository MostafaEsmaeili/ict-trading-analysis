using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>Creates a fresh stateless <see cref="TradeOrchestrator"/> for one symbol. The orchestrator is
/// per-symbol because its <see cref="EntryFillEvaluator"/> binds a per-symbol <see cref="SymbolSpec"/>; the
/// registry calls this on a cache miss. Production wiring resolves it as a singleton (the resolved options are
/// immutable).</summary>
public interface ITradeOrchestratorFactory
{
    TradeOrchestrator Create(Symbol symbol);
}
