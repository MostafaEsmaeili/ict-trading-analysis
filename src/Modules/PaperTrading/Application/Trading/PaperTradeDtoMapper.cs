using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.PaperTrading.Contracts;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// Pure mapping from the domain <see cref="PaperTrade"/> aggregate to the wire <see cref="PaperTradeDto"/>
/// (PaperTrading.Contracts) the module publishes on open/close. The enum-like wire fields carry the domain enum
/// MEMBER NAMES verbatim (via <see cref="Enum.ToString()"/>) so the dashboard/Gherkin contract stays
/// language-neutral and no literal is hand-typed here. The DTO is structurally advisory — it carries no order
/// field and routes nowhere (§6.3 guardrail).
///
/// <para><b>Two contract fields the domain aggregate does not yet carry — emitted as EXPLICITLY UNAVAILABLE.</b>
/// (1) <see cref="PaperTradeDto.SetupId"/> — the <see cref="PaperTrade"/> opens from a <c>TradePlan</c> and does not
/// retain its source setup id, so rather than emit a wrong value (e.g. the trade's own id, which would make any
/// downstream correlation-by-setup join on the wrong key) it is emitted as <see cref="Guid.Empty"/> = "unknown".
/// (2) <see cref="PaperTradeDto.Killzone"/> — the killzone is the scanner's session, not a trade field, so it maps to
/// <c>null</c> (the contract permits it). Emitting the TRUE source-setup id and killzone on EVERY event (incl. a
/// prior-candle trade's close, where no <c>Setup</c> is in scope) requires carrying both onto the
/// <see cref="PaperTrade"/> aggregate — a focused cross-aggregate enrichment to land before the Performance/Alerting
/// consumers segment paper-trade results by setup or killzone.</para>
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
            SetupId: Guid.Empty, // the trade carries no source-setup id yet — emit "unknown", never a wrong id
            Symbol: trade.Symbol.Value,
            // A TRADE side is "Long"/"Short" on the wire (the frozen TradeDirection contract the Gherkin + dashboard
            // assert), NOT the structural "Bullish"/"Bearish" of the underlying Direction — route through the converter.
            Direction: trade.Direction.ToTradeDirection().ToString(),
            Status: trade.Status.ToString(),
            Style: trade.Style.ToString(),
            Killzone: null, // the killzone is the scanner's session, not a field on the trade aggregate
            Entry: plan.Entry.Value,
            Stop: plan.Stop.Value,
            Targets: targets,
            Size: trade.Size.Lots,
            OpenedAtUtc: trade.OpenedAtUtc,
            ClosedAtUtc: trade.ClosedAtUtc,
            RealizedR: trade.RealizedR,
            // Management + P&L state the aggregate already owns — the enum-like fields carry the domain MEMBER names.
            Lifecycle: trade.Lifecycle.ToString(),
            CloseReason: trade.CloseReason?.ToString(),
            NetR: trade.NetR,
            GrossPnl: trade.GrossPnl?.Amount,
            Costs: trade.Costs?.Amount,
            NetPnl: trade.NetPnl?.Amount,
            HasScaledOut: trade.HasScaledOut,
            IsBreakevenArmed: trade.IsBreakevenArmed,
            RiskBudget: trade.RiskBudget.Amount,
            Timeframe: trade.Timeframe.ToString(),
            CurrentStop: trade.CurrentStop.Value,
            ExitPrice: trade.ExitPrice?.Value,
            ManagedFromUtc: trade.ManagedFromUtc);
    }

    /// <summary>
    /// Maps the trade to the wire DTO for an <c>OPEN</c> notification, forcing the open-state fields regardless of the
    /// aggregate's CURRENT state. A same-bar open-then-close (the −1R straddle / same-bar runner) raises both
    /// <c>PaperTradeOpened</c> and <c>PaperTradeClosed</c> in one advance, but the aggregate is already Closed by the
    /// time the events are drained — so mapping a single post-advance DTO onto BOTH events would ship a
    /// <c>PaperTradeOpened</c> carrying <c>Status=Closed</c> with the close fields populated. This overload keeps the
    /// open notification internally consistent (<see cref="TradeStatus.Open"/>, no <c>ClosedAtUtc</c>/<c>RealizedR</c>)
    /// so a future consumer that segments off <see cref="PaperTradeDto.Status"/> cannot misread an open as a close; the
    /// realized outcome flows on the separate <c>PaperTradeClosed</c> event (mapped via <see cref="ToDto"/>).
    /// </summary>
    public static PaperTradeDto ToOpenedDto(PaperTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        return ToDto(trade) with
        {
            Status = TradeStatus.Open.ToString(),
            Lifecycle = TradeLifecycle.Open.ToString(),
            ClosedAtUtc = null,
            RealizedR = null,
            // The realized outcome belongs only on the separate PaperTradeClosed event — keep an open notification
            // internally consistent so a consumer segmenting on Status can never misread the close fields.
            CloseReason = null,
            NetR = null,
            GrossPnl = null,
            Costs = null,
            NetPnl = null,
            ExitPrice = null,
            // Reset the management state to its at-open values too: in the same-bar open-then-close case the
            // aggregate is already past these by the time the events drain, so mapping the post-advance state onto
            // the OPEN notification would ship a trailed stop / breakeven-armed / scaled flag on a just-opened trade.
            CurrentStop = trade.Stop.Value,
            HasScaledOut = false,
            IsBreakevenArmed = false,
        };
    }
}
