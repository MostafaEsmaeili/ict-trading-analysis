using IctTrader.Domain.Common;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// A simulated, ADVISORY-sourced trade (plan §3.0/§5.1/§5.2/§2.5.9) — the aggregate root that expresses one
/// position's lifecycle independent of persistence and transport. Construction OPENS the trade at the plan's entry
/// (the §5.1 immediate-open path). It freezes <see cref="InitialRiskPerUnit"/> at open so realized R is always
/// measured against the original 1R (§5.2), and derives its <see cref="RiskBudget"/> from the same money geometry
/// it books P&amp;L with, so reserved risk and a stop-out loss can never disagree.
/// <para>
/// The position is closed through an append-only ledger of <see cref="FillLeg"/>s: an optional T1
/// <see cref="ScaleOut"/> books one partial leg over part of the size, then <see cref="Close"/> books the final
/// leg over the remainder. The trade's realized R and gross/net P&amp;L are DERIVED by folding the legs (one source
/// of truth: gross is the additive money sum and R = gross / risk-budget, so they cannot drift). The no-partial
/// path is identical to a single full-size close. Paper only — it writes to its own state, never routes an order
/// (§6.3). The stop-trail and time-exit management are deferred follow-on slices.
/// </para>
/// </summary>
public sealed class PaperTrade : AggregateRoot<Guid>
{
    private readonly decimal _pipSize;
    private readonly decimal _valuePerPip;
    private readonly decimal _valuePerPipForPosition;
    private readonly List<FillLeg> _legs = [];

    public PaperTrade(
        Guid id,
        Guid accountId,
        Symbol symbol,
        TradeStyle style,
        Timeframe timeframe,
        TradePlan plan,
        PositionSize size,
        decimal pipSize,
        decimal valuePerPip,
        DateTimeOffset openedAtUtc)
        : base(id)
    {
        Guard.Against(id == Guid.Empty, "PaperTrade requires a non-empty id.");
        Guard.Against(accountId == Guid.Empty, "PaperTrade requires the owning account id.");
        Guard.Against(symbol is null, "PaperTrade requires a symbol.");
        Guard.Against(pipSize <= 0m, "PaperTrade requires a positive pip size.");
        Guard.Against(valuePerPip <= 0m, "PaperTrade requires a positive value-per-pip.");
        Guard.Against(openedAtUtc.Offset != TimeSpan.Zero, "PaperTrade.OpenedAtUtc must be UTC.");

        var initialRiskPerUnit = Math.Abs(plan.Entry.Value - plan.Stop.Value);
        Guard.Against(initialRiskPerUnit <= 0m, "PaperTrade stop must differ from entry.");

        AccountId = accountId;
        Symbol = symbol!;
        Style = style;
        Timeframe = timeframe;
        Plan = plan;
        Size = size;
        RemainingSize = size;
        CurrentStop = plan.Stop;
        OpenedAtUtc = openedAtUtc;
        InitialRiskPerUnit = initialRiskPerUnit;
        Status = TradeStatus.Open;
        Lifecycle = TradeLifecycle.Open;

        _pipSize = pipSize;
        _valuePerPip = valuePerPip;
        _valuePerPipForPosition = valuePerPip * size.Lots;

        // The money at risk if the stop is hit, derived from the SAME geometry that books P&L, so the reserved
        // risk and a realized stop-out loss are guaranteed equal.
        RiskBudget = new Money(initialRiskPerUnit / pipSize * _valuePerPipForPosition);

        RaiseDomainEvent(new PaperTradeOpened(Id, AccountId, Symbol, Direction, Size, openedAtUtc));
    }

    public Guid AccountId { get; }

    public Symbol Symbol { get; }

    public TradeStyle Style { get; }

    public Timeframe Timeframe { get; }

    public TradePlan Plan { get; }

    /// <summary>The original sized position — the immutable denominator for cost, commission, and size weighting.</summary>
    public PositionSize Size { get; }

    /// <summary>While open, the lots not yet scaled out; after close, the lots the final leg closed.</summary>
    public PositionSize RemainingSize { get; private set; }

    /// <summary>The append-only exit ledger: the optional partial leg(s) plus the final leg. A read-only view so a
    /// caller cannot down-cast and mutate the ledger behind the aggregate's back.</summary>
    public IReadOnlyList<FillLeg> Legs => _legs.AsReadOnly();

    /// <summary>True once a partial scale-out has booked a leg before the final close.</summary>
    public bool HasScaledOut { get; private set; }

    /// <summary>The money at risk if the stop is hit — the trade's reserved share of the portfolio cap.</summary>
    public Money RiskBudget { get; }

    /// <summary>
    /// The value-per-pip for the WHOLE original position (per-pip value × original lots) — the money geometry the
    /// execution-cost model and the P&amp;L booking share, so a computed cost can never disagree with realized P&amp;L.
    /// </summary>
    public decimal ValuePerPipForPosition => _valuePerPipForPosition;

    public DateTimeOffset OpenedAtUtc { get; }

    /// <summary>The original |entry − stop| in price units, frozen at open so R is always vs the original 1R.</summary>
    public decimal InitialRiskPerUnit { get; }

    public TradeStatus Status { get; private set; }

    /// <summary>The richer §2.5.9 management state (Open → PartialTaken → Closed); Closed ⇔ Status is Closed.</summary>
    public TradeLifecycle Lifecycle { get; private set; }

    /// <summary>The final-leg exit price (back-compat for snapshot readers); set on close.</summary>
    public Price? ExitPrice { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    /// <summary>The final-leg close reason; set on close.</summary>
    public TradeCloseReason? CloseReason { get; private set; }

    /// <summary>The signed realized reward-to-risk in R, blended size-weighted across legs and measured on PRICE vs
    /// the frozen 1R. It is the strategy's structural edge and is NOT reduced by costs (§5.2); set on close.</summary>
    public decimal? RealizedR { get; private set; }

    /// <summary>The signed GROSS realized P&amp;L in account currency (before costs) — the additive sum of every
    /// leg's money; set on close.</summary>
    public Money? GrossPnl { get; private set; }

    /// <summary>The total §5.4 execution costs across every leg, deducted at close.</summary>
    public Money? Costs { get; private set; }

    /// <summary>The signed NET realized P&amp;L (gross − costs) — the figure booked to the account on settle.</summary>
    public Money? RealizedPnl { get; private set; }

    /// <summary>Alias for the booked net P&amp;L — the after-cost money result (§5.4).</summary>
    public Money? NetPnl => RealizedPnl;

    /// <summary>The after-cost reward-to-risk: net P&amp;L over the reserved risk budget (§5.4).</summary>
    public decimal? NetR => RealizedPnl is null ? null : RealizedPnl.Value.Amount / RiskBudget.Amount;

    public Direction Direction => Plan.Direction;

    public Price Entry => Plan.Entry;

    /// <summary>The ORIGINAL stop, frozen for the 1R geometry / snapshot readers. The live stop is <see cref="CurrentStop"/>.</summary>
    public Price Stop => Plan.Stop;

    /// <summary>The live stop after any ratchet (starts at the frozen <see cref="Stop"/>). The fill evaluator reads
    /// THIS, so a trailed stop governs the exit; <see cref="RiskBudget"/> and the <see cref="RealizedR"/> denominator
    /// stay the frozen original 1R, so a breakeven stop-out books ~0R rather than −1R (§5.2).</summary>
    public Price CurrentStop { get; private set; }

    /// <summary>True once the stop has been ratcheted to entry-or-better in the trade direction — the loss is capped
    /// off the original 1R. Derived (not a lifecycle state), so it composes with <see cref="HasScaledOut"/>.</summary>
    public bool IsBreakevenArmed => Direction == Direction.Bullish
        ? CurrentStop.Value >= Entry.Value
        : CurrentStop.Value <= Entry.Value;

    /// <summary>
    /// Books a T1 partial scale-out (plan §2.5.9): closes <paramref name="legSize"/> lots at the resting
    /// <paramref name="exitPrice"/> level, charging <paramref name="legCosts"/> on that leg, and reduces
    /// <see cref="RemainingSize"/>. Legal only once and only from an open, not-yet-scaled trade; the leg must close
    /// strictly fewer lots than remain (a full close is <see cref="Close"/>). It does NOT settle and does NOT touch
    /// the account — the partial's money lands on equity in the single terminal close.
    /// </summary>
    public void ScaleOut(
        Price exitPrice, PositionSize legSize, TradeCosts legCosts, TradeCloseReason reason, DateTimeOffset atUtc)
    {
        Guard.Against(Status != TradeStatus.Open, "Only an open paper trade can be scaled out.");
        Guard.Against(
            Lifecycle != TradeLifecycle.Open, "A paper trade can take only one partial scale-out in this model.");
        Guard.Against(atUtc.Offset != TimeSpan.Zero, "PaperTrade scale-out time must be UTC.");
        Guard.Against(atUtc < OpenedAtUtc, "A paper trade cannot scale out before it opened.");
        Guard.Against(
            legSize.Lots >= RemainingSize.Lots,
            "A partial scale-out must close strictly fewer lots than remain (a full close is Close).");

        var leg = new FillLeg(legSize, exitPrice, reason, legCosts, atUtc);
        _legs.Add(leg);
        RemainingSize = new PositionSize(Size.Lots - legSize.Lots);
        HasScaledOut = true;
        Lifecycle = TradeLifecycle.PartialTaken;

        var legGross = LegGross(leg);
        RaiseDomainEvent(new PaperTradePartialClosed(
            Id, AccountId, LegPriceR(leg), legGross, legCosts.Total, legGross - legCosts.Total,
            legSize.Lots / Size.Lots, legSize, RemainingSize, reason, atUtc));
    }

    /// <summary>
    /// Ratchets the protective stop toward profit (plan §2.5.9 stop management). Legal only from an open trade; the
    /// new stop must strictly TIGHTEN in the trade direction (a long stop only moves up, a short only down) and may
    /// cross entry to lock profit, but may not reach the runner target. It changes only <see cref="CurrentStop"/>
    /// (the level the fill evaluator honors) — the frozen <see cref="RiskBudget"/> and the <see cref="RealizedR"/>
    /// denominator are untouched, so R stays measured vs the original 1R. The WHEN/where decision (the trail ladder)
    /// is a candle-driven policy outside the aggregate (Slice C).
    /// </summary>
    public void MoveStop(Price newStop, DateTimeOffset atUtc)
    {
        Guard.Against(Status != TradeStatus.Open, "Only an open paper trade can move its stop.");
        Guard.Against(atUtc.Offset != TimeSpan.Zero, "PaperTrade stop-move time must be UTC.");
        Guard.Against(atUtc < OpenedAtUtc, "A paper trade cannot move its stop before it opened.");
        Guard.Against(
            _legs.Count > 0 && atUtc < _legs[^1].AtUtc,
            "A stop move cannot predate the last scale-out (the timeline must be monotonic).");

        var tightens = Direction == Direction.Bullish
            ? newStop.Value > CurrentStop.Value
            : newStop.Value < CurrentStop.Value;
        Guard.Against(!tightens, "A stop can only ratchet toward profit, never loosen.");

        var beforeRunner = Direction == Direction.Bullish
            ? newStop.Value < Plan.Targets.Runner.Value
            : newStop.Value > Plan.Targets.Runner.Value;
        Guard.Against(!beforeRunner, "A stop cannot trail to or past the runner target.");

        var previousStop = CurrentStop;
        CurrentStop = newStop;

        RaiseDomainEvent(new PaperTradeStopMoved(Id, AccountId, previousStop, newStop, IsBreakevenArmed, atUtc));
    }

    /// <summary>
    /// Closes the trade by booking the FINAL leg over <see cref="RemainingSize"/> at <paramref name="exitPrice"/>,
    /// then folding the blended totals over every leg. Legal only from an open trade (with or without a prior
    /// partial). <paramref name="costs"/> are this leg's §5.4 costs; <see cref="RealizedR"/> is the size-weighted
    /// price R (a no-partial stop is exactly −1R gross, a runner the plan RR) while <see cref="NetR"/> reflects the
    /// after-cost money result. The no-partial path reproduces a single full-size close exactly.
    /// </summary>
    public void Close(Price exitPrice, TradeCloseReason reason, TradeCosts costs, DateTimeOffset closedAtUtc)
    {
        Guard.Against(Status != TradeStatus.Open, "Only an open paper trade can be closed.");
        Guard.Against(closedAtUtc.Offset != TimeSpan.Zero, "PaperTrade.ClosedAtUtc must be UTC.");
        Guard.Against(closedAtUtc < OpenedAtUtc, "A paper trade cannot close before it opened.");
        Guard.Against(
            _legs.Count > 0 && closedAtUtc < _legs[^1].AtUtc,
            "A paper trade cannot close before its last scale-out (the leg timeline must be monotonic).");

        _legs.Add(new FillLeg(RemainingSize, exitPrice, reason, costs, closedAtUtc));

        // Fold the blended totals over ALL legs — ONE source of truth. Gross is the additive money sum; RealizedR is
        // DERIVED from it (gross / risk-budget), so the price-R and the money can never drift apart.
        var grossAmount = 0m;
        var costAmount = 0m;
        foreach (var leg in _legs)
        {
            grossAmount += LegGross(leg).Amount;
            costAmount += leg.Costs.Total.Amount;
        }

        var grossPnl = new Money(grossAmount);
        var totalCosts = new Money(costAmount);
        var netPnl = grossPnl - totalCosts;
        var realizedR = grossPnl.Amount / RiskBudget.Amount;

        ExitPrice = exitPrice;
        CloseReason = reason;
        ClosedAtUtc = closedAtUtc;
        GrossPnl = grossPnl;
        Costs = totalCosts;
        RealizedPnl = netPnl;
        RealizedR = realizedR;
        Status = TradeStatus.Closed;
        Lifecycle = TradeLifecycle.Closed;

        var netR = netPnl.Amount / RiskBudget.Amount;
        RaiseDomainEvent(new PaperTradeClosed(
            Id, AccountId, realizedR, netR, grossPnl, totalCosts, netPnl, reason, closedAtUtc));
    }

    /// <summary>The signed gross money of one leg: its price move over the frozen pip geometry, weighted by its lots.</summary>
    private Money LegGross(FillLeg leg)
    {
        var signedMove = Direction == Direction.Bullish
            ? leg.ExitPrice.Value - Entry.Value
            : Entry.Value - leg.ExitPrice.Value;
        return new Money(signedMove / _pipSize * _valuePerPip * leg.Lots.Lots);
    }

    /// <summary>The leg's full-size-equivalent price R (e.g. +1R at T1, +3R at the runner) for the partial event.</summary>
    private decimal LegPriceR(FillLeg leg)
    {
        var signedMove = Direction == Direction.Bullish
            ? leg.ExitPrice.Value - Entry.Value
            : Entry.Value - leg.ExitPrice.Value;
        return signedMove / InitialRiskPerUnit;
    }
}
