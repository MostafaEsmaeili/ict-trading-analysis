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
/// The ONE simulated-open path (plan §5.1) the BOTH triggers — the automatic <see cref="SetupConfirmedHandler"/> and
/// the operator-driven <c>TakeSetupCommandHandler</c> — share. It rehydrates the domain <see cref="Domain.Setups.Setup"/>
/// from the wire <see cref="SetupDto"/>, loads-or-creates the demo <see cref="PaperAccount"/>, resolves the per-symbol
/// geometry (§2.5.7), feeds the §2.4/§2.5.5 daily-risk-guard input, and hands the setup to the per-symbol
/// <see cref="TradeOrchestrator"/> which DECIDES arm (reserve) vs open (register) vs suppress. The mutated account +
/// the new armed entry / trade are committed in one <see cref="IPaperTradingUnitOfWork.SaveChangesAsync"/>; an
/// Immediate open publishes <see cref="Contracts.PaperTradeOpened"/>.
///
/// <para><b>WHY extract it.</b> Auto and Take MUST be byte-identical in sizing, the §2.5.10 portfolio cap, and the
/// paper-only guardrail — so the open is written ONCE here and both callers invoke it. There is no second open path,
/// no broker/order/executor anywhere: this only writes to our own aggregates (§6.3 guardrail). The caller owns the
/// idempotency short-circuit (a trade/armed entry already exists under the deterministic <see cref="SetupDto.Id"/>);
/// this opener assumes it has been admitted.</para>
///
/// <para>It is a module collaborator (not part of the module's Contracts surface) — the two public handlers inject it,
/// so it is <c>public</c> only because C# forbids a public ctor parameter from being less accessible than the ctor; it
/// is never referenced outside this module.</para>
/// </summary>
public sealed class SetupTradeOpener
{
    private readonly ITradeOrchestratorRegistry _registry;
    private readonly IPaperAccountProvider _accountProvider;
    private readonly IInstrumentRegistry _instruments;
    private readonly IPaperTradeRepository _trades;
    private readonly IArmedEntryRepository _armedEntries;
    private readonly IPaperTradingUnitOfWork _unitOfWork;
    private readonly IMessageBus _bus;
    private readonly ConfluenceOptions _grading;
    private readonly DailyRiskGuardOptions _dailyGuard;

    // Used ONLY for §2.1 NY-date conversion of the daily-guard tally — "now" is the setup's ConfirmedAtUtc, never an
    // ambient clock, so NewYorkDate is a pure conversion independent of the TimeProvider (mirrors the prior handler).
    private readonly Domain.Sessions.NyClock _nyClock = new(TimeProvider.System);

    public SetupTradeOpener(
        ITradeOrchestratorRegistry registry,
        IPaperAccountProvider accountProvider,
        IInstrumentRegistry instruments,
        IPaperTradeRepository trades,
        IArmedEntryRepository armedEntries,
        IPaperTradingUnitOfWork unitOfWork,
        IMessageBus bus,
        IOptions<ConfluenceOptions> grading,
        IOptions<DailyRiskGuardOptions> dailyGuard)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        _instruments = instruments ?? throw new ArgumentNullException(nameof(instruments));
        _trades = trades ?? throw new ArgumentNullException(nameof(trades));
        _armedEntries = armedEntries ?? throw new ArgumentNullException(nameof(armedEntries));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _grading = (grading ?? throw new ArgumentNullException(nameof(grading))).Value;
        _dailyGuard = (dailyGuard ?? throw new ArgumentNullException(nameof(dailyGuard))).Value;
    }

    /// <summary>
    /// Opens (Immediate) or arms (Armed) ONE simulated position from the confirmed advisory setup, exactly as the
    /// automatic path always did. Returns the opened wire DTO when a trade opened immediately (so the Take command can
    /// echo it to the operator); <c>null</c> when the orchestrator armed a resting limit or the daily-risk guard
    /// suppressed the setup (nothing to echo). The deterministic <see cref="SetupDto.Id"/> becomes the opened/armed
    /// aggregate id, so the seam stays idempotent.
    /// </summary>
    public async Task<PaperTradeDto?> OpenAsync(SetupDto setupDto, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setupDto);

        var setupId = setupDto.Id;
        var setup = SetupRehydrator.ToDomain(setupDto, _grading);
        var account = await _accountProvider.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);

        // Per-instrument resolution (§2.5.7): NAS100USD sizes with index point geometry, an FX major with FxMajor
        // geometry — so a counter-class symbol can never be mis-sized.
        var profile = _instruments.Resolve(setup.Symbol);

        // §2.4/§2.5.5 daily risk guard input (null when disabled): the account's net realized P&L over trades closed
        // earlier on this NY trading day. A halted day makes OnSetupConfirmed return ManagedPosition.None below.
        var dayRealizedPnl = await DayRealizedPnlAsync(setup.ConfirmedAtUtc, cancellationToken).ConfigureAwait(false);

        // The domain DECIDES: arm (reserve) or open (register) — both mutate the account ledger. The deterministic
        // seam id is threaded in so the opened/armed aggregate carries it (the idempotency key the caller reads).
        var position = _registry
            .GetOrCreate(setup.Symbol)
            .OnSetupConfirmed(
                setup, account, profile.SymbolSpec, profile.ContractSpec, setup.ConfirmedAtUtc, setupId, dayRealizedPnl);

        if (position.IsSuppressed)
        {
            // The daily risk guard declined the day — nothing was armed/opened and no risk was reserved.
            return null;
        }

        if (position.Trade is not null)
        {
            // Immediate mode: a trade opened directly. Stage it and publish the open. ToDto here is byte-identical to
            // the prior SetupConfirmedHandler (a just-opened trade is Status=Open with no close fields anyway).
            var dto = PaperTradeDtoMapper.ToDto(position.Trade);
            await _trades.AddAsync(position.Trade, cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Clear the open event so the next candle dispatch (which loads this same trade under DB-as-state) does
            // not re-publish it — a real EF reload yields a fresh aggregate; clearing matches that once-only semantics.
            position.Trade.ClearDomainEvents();
            await _bus.PublishAsync(new Contracts.PaperTradeOpened(dto), cancellationToken).ConfigureAwait(false);
            return dto;
        }

        // Armed mode: a resting limit is reserved. No contract event yet — the open fires when a candle triggers it.
        await _armedEntries.AddAsync(position.Armed!, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return null;
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
            // No-look-ahead (§4.1): a trade that closed AFTER this setup's confirm time must not count toward its
            // daily tally — require closedAt ≤ nowUtc in addition to the NY-date match (consistent across replays).
            if (trade.ClosedAtUtc is { } closedAt && closedAt <= nowUtc
                && _nyClock.NewYorkDate(closedAt) == nyDate && trade.NetPnl is { } pnl)
            {
                sum += pnl.Amount;
            }
        }

        return new Money(sum);
    }
}
