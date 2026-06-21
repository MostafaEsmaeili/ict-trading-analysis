using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// Decides whether one <see cref="Candle"/> fills a confirmed <see cref="Setup"/>'s resting entry LIMIT (plan
/// §2.5.1 step 7): price must RETRACE into the OTE/FVG entry zone for the limit to fill — a setup that never
/// retraces is a no-fill (ICT: "we don't chase it"). The DECIDE half, mirroring <see cref="IFillEvaluator"/>;
/// pure and clock-free. The armed-order lifecycle, the no-chase cancellation, the same-bar entry-then-stop −1R
/// straddle, and the EntryMode wiring are follow-on cuts.
/// </summary>
public interface IEntryFillEvaluator
{
    EntryFillDecision Evaluate(Setup setup, Candle candle);
}
