using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// Builds a <see cref="TradeOrchestrator"/> for one symbol from the host's validated <c>Ict:*</c> options (each
/// already bound + <c>ValidateOnStart</c>-gated). It snapshots the <see cref="IOptions{T}.Value"/> set once into
/// immutable fields, then assembles the exact object graph the proven <c>TradeOrchestratorTests</c> pins: an
/// <see cref="EntryManager"/> (entry-fill + fill + cost model + killzone clock), an <see cref="ExitManager"/>
/// (fill + stop-trail + cost model + NY clock), a <see cref="PaperTradeFactory"/> (risk options + adaptive
/// <see cref="RiskManager"/>), composed into the orchestrator with the shared <see cref="EntryManagementOptions"/>.
///
/// <para>The orchestrator is PER-SYMBOL because <see cref="EntryFillEvaluator"/> binds a per-symbol
/// <see cref="SymbolSpec"/>; everything else is stateless DECIDE/APPLY, so a cached orchestrator per symbol is
/// safe. The same <see cref="EntryManagementOptions"/> instance is shared by the entry-fill evaluator, the entry
/// manager, and the orchestrator (one source of truth for the entry mode + no-chase backstop).</para>
/// </summary>
public sealed class TradeOrchestratorFactory : ITradeOrchestratorFactory
{
    private readonly TimeProvider _timeProvider;
    private readonly FillOptions _fill;
    private readonly ExecutionCostOptions _executionCost;
    private readonly StopTrailOptions _stopTrail;
    private readonly ExitManagementOptions _exitManagement;
    private readonly EntryManagementOptions _entryManagement;
    private readonly KillzoneEntryOptions _killzoneEntry;
    private readonly TradeStyleOptions _tradeStyles;
    private readonly RiskOptions _risk;

    public TradeOrchestratorFactory(
        TimeProvider timeProvider,
        IOptions<FillOptions> fill,
        IOptions<ExecutionCostOptions> executionCost,
        IOptions<StopTrailOptions> stopTrail,
        IOptions<ExitManagementOptions> exitManagement,
        IOptions<EntryManagementOptions> entryManagement,
        IOptions<KillzoneEntryOptions> killzoneEntry,
        IOptions<TradeStyleOptions> tradeStyles,
        IOptions<RiskOptions> risk)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _fill = fill.Value;
        _executionCost = executionCost.Value;
        _stopTrail = stopTrail.Value;
        _exitManagement = exitManagement.Value;
        _entryManagement = entryManagement.Value;
        _killzoneEntry = killzoneEntry.Value;
        _tradeStyles = tradeStyles.Value;
        _risk = risk.Value;
    }

    public TradeOrchestrator Create(Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var spec = SymbolSpec.FxMajor(symbol);

        var entryManager = new EntryManager(
            new EntryFillEvaluator(_entryManagement, spec),
            new FillEvaluator(_fill),
            new ExecutionCostModel(_executionCost),
            new KillzoneClock(new NyClock(_timeProvider), KillzoneSchedule.CreateDefault()),
            _killzoneEntry,
            _entryManagement);

        var exitManager = new ExitManager(
            new FillEvaluator(_fill),
            new StopTrailPolicy(_stopTrail),
            new ExecutionCostModel(_executionCost),
            _exitManagement,
            new NyClock(_timeProvider),
            _tradeStyles);

        var factory = new PaperTradeFactory(_risk, new RiskManager());

        return new TradeOrchestrator(entryManager, exitManager, factory, _entryManagement);
    }
}
