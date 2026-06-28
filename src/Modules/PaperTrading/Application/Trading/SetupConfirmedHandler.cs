using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.Options;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// The PaperTrading module's setup→trade seam (plan §3.0a/§4.1): it reacts to each <see cref="SetupConfirmed"/>,
/// rehydrates the domain <see cref="Domain.Setups.Setup"/> from the wire DTO, loads-or-creates the single demo
/// <see cref="PaperAccount"/>, and hands the setup to the per-symbol <see cref="TradeOrchestrator"/>. In the default
/// Armed mode the orchestrator rests a limit and RESERVES its risk against the account's portfolio cap; in Immediate
/// mode it OPENS the trade and registers it. Either way the account is MUTATED, so it is committed (with the new
/// armed entry or trade) in one <see cref="IPaperTradingUnitOfWork.SaveChangesAsync"/>.
///
/// <para>The handler ORCHESTRATES only — every decision (sizing, the cap gate, arm-vs-open) lives in the pure domain
/// it drives. It is the bus-scoped per-dispatch unit of work: each dispatch runs in its own DI scope with its own
/// context, so the demo account is loaded FRESH and tracked by THIS scope's context (DB-as-state — no aggregate is
/// cached across dispatches). An Immediate open publishes <see cref="Contracts.PaperTradeOpened"/>; an Armed entry
/// publishes no contract event yet — the open fires when a candle triggers it (the candle handler's job).</para>
/// </summary>
public sealed class SetupConfirmedHandler(
    ITradeOrchestratorRegistry registry,
    IPaperAccountProvider accountProvider,
    IInstrumentRegistry instruments,
    IPaperTradeRepository trades,
    IArmedEntryRepository armedEntries,
    IPaperTradingUnitOfWork unitOfWork,
    IMessageBus bus,
    IOptions<ConfluenceOptions> grading,
    IOptions<DailyRiskGuardOptions> dailyGuard)
    : IEventHandler<SetupConfirmed>
{
    private readonly ITradeOrchestratorRegistry _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));

    private readonly IPaperAccountProvider _accountProvider =
        accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));

    private readonly IInstrumentRegistry _instruments =
        instruments ?? throw new ArgumentNullException(nameof(instruments));

    private readonly IPaperTradeRepository _trades = trades ?? throw new ArgumentNullException(nameof(trades));

    private readonly IArmedEntryRepository _armedEntries =
        armedEntries ?? throw new ArgumentNullException(nameof(armedEntries));

    private readonly IPaperTradingUnitOfWork _unitOfWork =
        unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));

    private readonly ConfluenceOptions _grading =
        (grading ?? throw new ArgumentNullException(nameof(grading))).Value;

    private readonly DailyRiskGuardOptions _dailyGuard =
        (dailyGuard ?? throw new ArgumentNullException(nameof(dailyGuard))).Value;

    // Used ONLY for §2.1 NY-date timezone conversion of trade close times — the "now" comes from the setup's
    // ConfirmedAtUtc, never an ambient clock, so NewYorkDate is a pure conversion independent of the TimeProvider.
    private readonly Domain.Sessions.NyClock _nyClock = new(TimeProvider.System);

    public async Task HandleAsync(SetupConfirmed @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Idempotency on the deterministic seam key (SetupDto.Id): a replayed / redelivered / restart-re-streamed
        // SetupConfirmed for the SAME setup must NOT open a second trade (double-reserving the ~5% cap, double-counting
        // performance). The id becomes the opened/armed aggregate id below, so an existing trade OR armed entry under it
        // means this setup is already handled — short-circuit BEFORE touching the account/orchestrator.
        var setupId = @event.Setup.Id;
        if (await _trades.GetByIdAsync(setupId, cancellationToken).ConfigureAwait(false) is not null
            || await _armedEntries.GetByIdAsync(setupId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        var setup = SetupRehydrator.ToDomain(@event.Setup, _grading);
        var account = await _accountProvider.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);

        // Per-instrument resolution (§2.5.7): NAS100USD sizes with index point geometry (1.0/point, 1-unit lots),
        // an FX major with the existing FxMajor money geometry — so a counter-class symbol can never be mis-sized.
        var profile = _instruments.Resolve(setup.Symbol);
        var symbolSpec = profile.SymbolSpec;
        var contractSpec = profile.ContractSpec;

        // §2.4/§2.5.5 daily risk guard input (null when disabled): the account's net realized P&L over trades closed
        // earlier on this NY trading day. A halted day makes OnSetupConfirmed return ManagedPosition.None below.
        var dayRealizedPnl = await DayRealizedPnlAsync(setup.ConfirmedAtUtc, cancellationToken).ConfigureAwait(false);

        // The domain DECIDES: arm (reserve) or open (register) — both mutate the account ledger. The deterministic
        // seam id is threaded in so the opened/armed aggregate carries it (the idempotency key the guard above reads).
        var position = _registry
            .GetOrCreate(setup.Symbol)
            .OnSetupConfirmed(setup, account, symbolSpec, contractSpec, setup.ConfirmedAtUtc, setupId, dayRealizedPnl);

        if (position.IsSuppressed)
        {
            // The daily risk guard declined the day — nothing was armed/opened and no risk was reserved. The setup
            // stays a graded advisory we simply did not act on; persist/publish nothing.
            return;
        }

        if (position.Trade is not null)
        {
            // Immediate mode: a trade opened directly. Stage it and publish the open.
            var dto = PaperTradeDtoMapper.ToDto(position.Trade);
            await _trades.AddAsync(position.Trade, cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Clear the open event so the next candle dispatch (which loads this same trade under DB-as-state) does
            // not re-publish it — a real EF reload yields a fresh aggregate; clearing matches that once-only semantics.
            position.Trade.ClearDomainEvents();
            await _bus
                .PublishAsync(new Contracts.PaperTradeOpened(dto), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // Armed mode: a resting limit is reserved. No contract event yet — the open fires when a candle triggers it.
        await _armedEntries.AddAsync(position.Armed!, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The account's net realized P&amp;L over trades CLOSED on the same NY trading day as <paramref name="nowUtc"/>
    /// (the §2.4/§2.5.5 daily risk guard input). Returns null when the guard is disabled so the unguarded path skips the
    /// repo read entirely and stays byte-identical.</summary>
    private async Task<Money?> DayRealizedPnlAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (!_dailyGuard.Enabled)
        {
            return null;
        }

        var nyDate = _nyClock.NewYorkDate(nowUtc);
        var closed = await _trades.GetClosedAsync(cancellationToken).ConfigureAwait(false);
        var sum = 0m;
        foreach (var trade in closed)
        {
            // No-look-ahead (§4.1): on a replay/backfill the repo can hold trades closed LATER on the same NY day; a
            // trade that closed AFTER this setup's confirm time must not count toward its daily tally. Require closedAt
            // ≤ nowUtc in addition to the NY-date match so the guard stays consistent across replays and restarts.
            if (trade.ClosedAtUtc is { } closedAt && closedAt <= nowUtc
                && _nyClock.NewYorkDate(closedAt) == nyDate && trade.NetPnl is { } pnl)
            {
                sum += pnl.Amount;
            }
        }

        return new Money(sum);
    }
}
