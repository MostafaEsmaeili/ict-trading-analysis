using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// Resolves one OPEN <see cref="PaperTrade"/> against one <see cref="Candle"/> into a <see cref="FillDecision"/>
/// (plan §5.2). PURE: no I/O, no ambient clock, no mutation — the domain decides, the caller applies. This slice
/// resolves the exit leg only (stop / runner); entry-arming (Pending→Open), partial scale-outs, breakeven arming,
/// time-exit and the §5.4 cost model are deferred follow-ons.
/// </summary>
public interface IFillEvaluator
{
    FillDecision Evaluate(PaperTrade trade, Candle candle);
}
