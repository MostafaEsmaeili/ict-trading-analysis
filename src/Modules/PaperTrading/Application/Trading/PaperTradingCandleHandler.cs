using IctTrader.Domain.Repositories;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;
using IctTrader.PaperTrading.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// The PaperTrading module's per-candle management seam (plan §3.0a/§4.1): it reacts to each
/// <see cref="CandleIngested"/> and ADVANCES every live position for that symbol one bar through the pure domain
/// <see cref="TradeOrchestrator"/> — filling resting limits, scaling, trailing, and closing on a stop/runner/time
/// exit. The domain DECIDES (entry/exit machinery) and SETTLES every terminal close itself (the orchestrator calls
/// <see cref="PaperAccount.Settle"/>); the handler only orchestrates loading, persisting, and publishing.
///
/// <para><b>DB-as-state (plan §4.1).</b> Each dispatch runs in its own DI scope with its own context, so no aggregate
/// is cached across dispatches. The active armed entries (<see cref="IArmedEntryRepository.GetActiveAsync"/>) and
/// open trades (<see cref="IPaperTradeRepository.GetOpenAsync"/>) ARE the warm-start set: they are loaded FRESH and
/// tracked by THIS scope's context, reconstructed into a <see cref="ManagedPosition"/> per aggregate, advanced, and
/// saved. This is both scope-safe (no detached entities) and restart-safe (a crash resumes from the database).</para>
///
/// <para><b>Event mapping.</b> Each aggregate raises domain events for what happened this bar; the handler drains
/// them and translates the terminal ones to the FROZEN PaperTrading.Contracts events — a resting limit that opened
/// publishes <see cref="Contracts.PaperTradeOpened"/>, a trade that closed publishes
/// <see cref="Contracts.PaperTradeClosed"/> with the outcome derived from its close reason. A same-bar
/// open-then-close (the −1R straddle / same-bar runner) publishes BOTH, in order.</para>
/// </summary>
public sealed class PaperTradingCandleHandler(
    ITradeOrchestratorRegistry registry,
    IPaperAccountProvider accountProvider,
    IPaperTradeRepository trades,
    IArmedEntryRepository armedEntries,
    IPaperTradingUnitOfWork unitOfWork,
    IMessageBus bus)
    : IEventHandler<CandleIngested>
{
    private readonly ITradeOrchestratorRegistry _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));

    private readonly IPaperAccountProvider _accountProvider =
        accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));

    private readonly IPaperTradeRepository _trades = trades ?? throw new ArgumentNullException(nameof(trades));

    private readonly IArmedEntryRepository _armedEntries =
        armedEntries ?? throw new ArgumentNullException(nameof(armedEntries));

    private readonly IPaperTradingUnitOfWork _unitOfWork =
        unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));

    public async Task HandleAsync(CandleIngested @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var symbol = new Symbol(@event.Candle.Symbol);
        var candle = CandleToDomainMapper.ToDomain(@event.Candle);
        var barCloseUtc = CandleToDomainMapper.BarCloseUtc(@event.Candle);

        var active = await LoadActivePositionsAsync(symbol, candle.OpenTimeUtc, cancellationToken).ConfigureAwait(false);
        if (active.Count == 0)
        {
            return;
        }

        var account = await _accountProvider.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
        var orchestrator = _registry.GetOrCreate(symbol);

        // Advance every live position one bar. The orchestrator applies the entry/exit plans and settles terminal
        // closes against the (tracked) account; the new trade for a just-triggered armed entry is staged below. The
        // contract events are collected as the CONCRETE record types (not IEvent) so the bus's generic PublishAsync
        // binds each handler correctly, then published only AFTER the unit of work commits (events react to a
        // persisted change, plan §3.0a) and outside the load loop.
        var opened = new List<Contracts.PaperTradeOpened>();
        var closed = new List<Contracts.PaperTradeClosed>();
        foreach (var position in active)
        {
            var openBeforeAdvance = position.Trade is not null;

            orchestrator.Advance(position, account, candle, barCloseUtc);

            await StageNewTradeAsync(position, openBeforeAdvance, cancellationToken).ConfigureAwait(false);
            CollectContractEvents(position, opened, closed);

            // Clear the drained domain events so a still-open trade carried to the NEXT candle (loaded as the same
            // tracked instance under DB-as-state) does not re-publish this bar's events. A real EF reload yields a
            // fresh aggregate with no events; clearing makes the in-memory path match that once-only semantics.
            position.Trade?.ClearDomainEvents();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var openedEvent in opened)
        {
            await _bus.PublishAsync(openedEvent, cancellationToken).ConfigureAwait(false);
        }

        foreach (var closedEvent in closed)
        {
            await _bus.PublishAsync(closedEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Loads the symbol's active armed entries + open trades FRESH (tracked by this scope's context) and
    /// reconstructs a <see cref="ManagedPosition"/> per aggregate — the DB-as-state warm-start set.
    ///
    /// <para><b>No same-bar look-ahead (plan §4.1).</b> A setup confirmed on candle N is opened/armed mid-dispatch by
    /// <see cref="SetupConfirmedHandler"/> with <see cref="PaperTrade.OpenedAtUtc"/> / <see cref="ArmedEntry.ArmedAtUtc"/>
    /// stamped at candle N's open. The Scanning handler fans out before this one on the SAME <see cref="CandleIngested"/>,
    /// so that just-created position is already persisted when this method reloads. Advancing it on candle N would let its
    /// entry limit fill — or its stop/runner hit — on the very bar that produced the signal: look-ahead bias, since live
    /// the order could not have been resting during the bar that generated it. So a position is eligible for management
    /// only from the bar AFTER its arm/open bar — filter to those created STRICTLY BEFORE this candle's open. (This is a
    /// different concern from the legitimate same-bar entry-then-exit re-feed INSIDE
    /// <see cref="TradeOrchestrator.Advance"/>, which is left untouched.)</para></summary>
    private async Task<IReadOnlyList<ManagedPosition>> LoadActivePositionsAsync(
        Symbol symbol, DateTimeOffset candleOpenUtc, CancellationToken cancellationToken)
    {
        var positions = new List<ManagedPosition>();

        var resting = await _armedEntries.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        foreach (var armed in resting.Where(a => a.Symbol == symbol && a.ArmedAtUtc < candleOpenUtc))
        {
            positions.Add(ManagedPosition.Resting(armed));
        }

        var open = await _trades.GetOpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var trade in open.Where(t => t.Symbol == symbol && t.OpenedAtUtc < candleOpenUtc))
        {
            positions.Add(ManagedPosition.Live(trade));
        }

        return positions;
    }

    /// <summary>Stages the trade a resting limit just triggered into (it was reconstructed from an armed entry, so
    /// the new <see cref="PaperTrade"/> aggregate is not yet tracked). A position that began as a live trade is
    /// already tracked, so nothing is staged for it.</summary>
    private async Task StageNewTradeAsync(
        ManagedPosition position, bool openBeforeAdvance, CancellationToken cancellationToken)
    {
        if (!openBeforeAdvance && position.Trade is not null)
        {
            await _trades.AddAsync(position.Trade, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Drains the aggregate's raised domain events and translates the terminal ones to the frozen contract
    /// events. A just-opened trade → <see cref="Contracts.PaperTradeOpened"/>; a closed trade →
    /// <see cref="Contracts.PaperTradeClosed"/>. A same-bar open-then-close yields both. Each event carries a DTO
    /// snapshotted for ITS transition — the open notification reports <see cref="TradeStatus.Open"/> (via
    /// <see cref="PaperTradeDtoMapper.ToOpenedDto"/>) and the close carries the realized outcome (via
    /// <see cref="PaperTradeDtoMapper.ToDto"/>) — so a same-bar straddle never ships a "PaperTradeOpened" that says
    /// Closed, and a consumer that segments off <see cref="PaperTradeDto.Status"/> reads each event coherently.</summary>
    private static void CollectContractEvents(
        ManagedPosition position,
        List<Contracts.PaperTradeOpened> opened,
        List<Contracts.PaperTradeClosed> closed)
    {
        if (position.Trade is null)
        {
            return; // a still-resting or cancelled limit raised no trade contract event
        }

        var trade = position.Trade;

        foreach (var domainEvent in trade.DomainEvents)
        {
            switch (domainEvent)
            {
                case Domain.Trading.PaperTradeOpened:
                    opened.Add(new Contracts.PaperTradeOpened(PaperTradeDtoMapper.ToOpenedDto(trade)));
                    break;

                case Domain.Trading.PaperTradeClosed close:
                    closed.Add(new Contracts.PaperTradeClosed(PaperTradeDtoMapper.ToDto(trade), OutcomeOf(close.Reason)));
                    break;
            }
        }
    }

    /// <summary>Maps the domain close reason to the wire outcome string (member name) — no magic string.</summary>
    private static string OutcomeOf(TradeCloseReason reason) => reason.ToString();
}
