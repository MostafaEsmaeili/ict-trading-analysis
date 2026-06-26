using IctTrader.Domain.Trading;
using IctTrader.PaperTrading.Contracts;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// Pure mapping from the domain <see cref="PaperTrade"/> aggregate to the wire <see cref="PaperTradeDto"/>
/// (PaperTrading.Contracts) the module publishes on open/close. The enum-like wire fields carry the domain enum
/// MEMBER NAMES verbatim (via <see cref="Enum.ToString()"/>) so the dashboard/Gherkin contract stays
/// language-neutral and no literal is hand-typed here. The DTO is structurally advisory — it carries no order
/// field and routes nowhere (§6.3 guardrail).
///
/// <para><b>Two contract fields the domain aggregate does not yet carry — PLACEHOLDERS (deferred enrichment).</b>
/// (1) <see cref="PaperTradeDto.SetupId"/> — the <see cref="PaperTrade"/> opens from a <c>TradePlan</c> and does not
/// retain its source setup id, so the trade's own id is emitted as a stable correlation key (for an armed trade it
/// equals the reservation/armed-entry id). (2) <see cref="PaperTradeDto.Killzone"/> — the killzone is the scanner's
/// session, not a trade field, so it maps to <c>null</c> (the contract permits it). Emitting the TRUE source-setup id
/// and killzone on EVERY event (incl. a prior-candle trade's close, where no <c>Setup</c> is in scope) requires
/// carrying both onto the <see cref="PaperTrade"/> aggregate — a focused cross-aggregate enrichment to land before the
/// Performance/Alerting consumers segment paper-trade results by setup or killzone.</para>
/// </summary>
internal static class PaperTradeDtoMapper
{
    public static PaperTradeDto ToDto(PaperTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        var plan = trade.Plan;
        var targets = plan.Targets.Targets.Select(price => price.Value).ToArray();

        return new PaperTradeDto(
            Id: trade.Id,
            SetupId: trade.Id, // the trade carries no source-setup id; its own id is the stable correlation key
            Symbol: trade.Symbol.Value,
            Direction: trade.Direction.ToString(),
            Status: trade.Status.ToString(),
            Style: trade.Style.ToString(),
            Killzone: null, // the killzone is the scanner's session, not a field on the trade aggregate
            Entry: plan.Entry.Value,
            Stop: plan.Stop.Value,
            Targets: targets,
            Size: trade.Size.Lots,
            OpenedAtUtc: trade.OpenedAtUtc,
            ClosedAtUtc: trade.ClosedAtUtc,
            RealizedR: trade.RealizedR);
    }
}
