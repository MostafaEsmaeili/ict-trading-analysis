using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
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
    private readonly IInstrumentRegistry _instruments;
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
        IInstrumentRegistry instruments,
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
        ArgumentNullException.ThrowIfNull(instruments);
        _timeProvider = timeProvider;
        _instruments = instruments;
        _fill = fill.Value;
        _executionCost = executionCost.Value;
        _stopTrail = stopTrail.Value;
        _exitManagement = exitManagement.Value;
        _entryManagement = entryManagement.Value;
        _killzoneEntry = killzoneEntry.Value;
        _tradeStyles = tradeStyles.Value;
        _risk = risk.Value;
    }

    public TradeOrchestrator Create(Symbol symbol, RiskOptions? risk = null)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        // Per-instrument resolution (§2.5.7): the catalog yields the symbol's price + money geometry and its
        // per-class overrides. For an Index symbol the cost model books point-based spread + 0 commission and the
        // entry band reads the index tolerance; an FX major's `None` bundle leaves every option field-equal, so the
        // FX orchestrator is byte-identical to the prior hardcoded FxMajor path.
        var profile = _instruments.Resolve(symbol);
        var spec = profile.SymbolSpec;
        var contract = profile.ContractSpec;
        var executionCost = _executionCost.WithInstrumentOverrides(profile.Overrides);
        var entryManagement = _entryManagement.WithInstrumentOverrides(profile.Overrides);
        // A per-run RiskOptions (the on-demand backtest's sizing) overrides the host default; the per-instrument
        // scalar overrides still apply on top so an index keeps its point-based min-stop either way.
        var resolvedRisk = (risk ?? _risk).WithInstrumentOverrides(profile.Overrides);

        var entryManager = new EntryManager(
            new EntryFillEvaluator(entryManagement, spec),
            new FillEvaluator(_fill),
            new ExecutionCostModel(executionCost),
            new KillzoneClock(new NyClock(_timeProvider), KillzoneSchedule.CreateDefault()),
            _killzoneEntry,
            entryManagement);

        var exitManager = new ExitManager(
            new FillEvaluator(_fill),
            new StopTrailPolicy(_stopTrail),
            new ExecutionCostModel(executionCost),
            _exitManagement,
            new NyClock(_timeProvider),
            _tradeStyles,
            contract);

        var factory = new PaperTradeFactory(resolvedRisk, new RiskManager());

        // The orchestrator shares the SAME resolved EntryManagementOptions the entry manager + entry-fill evaluator
        // use (one source of truth for the entry mode + no-chase backstop + the close-proximity band).
        return new TradeOrchestrator(entryManager, exitManager, factory, entryManagement);
    }
}
