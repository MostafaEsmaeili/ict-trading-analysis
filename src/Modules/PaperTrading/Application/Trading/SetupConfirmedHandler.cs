using IctTrader.Domain.Configuration;
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
    IPaperTradeRepository trades,
    IArmedEntryRepository armedEntries,
    IPaperTradingUnitOfWork unitOfWork,
    IMessageBus bus,
    IOptions<ConfluenceOptions> grading)
    : IEventHandler<SetupConfirmed>
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

    private readonly ConfluenceOptions _grading =
        (grading ?? throw new ArgumentNullException(nameof(grading))).Value;

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

        var symbolSpec = SymbolSpec.FxMajor(setup.Symbol);
        var contractSpec = ContractSpec.FxMajor(setup.Symbol);

        // The domain DECIDES: arm (reserve) or open (register) — both mutate the account ledger. The deterministic
        // seam id is threaded in so the opened/armed aggregate carries it (the idempotency key the guard above reads).
        var position = _registry
            .GetOrCreate(setup.Symbol)
            .OnSetupConfirmed(setup, account, symbolSpec, contractSpec, setup.ConfirmedAtUtc, setupId);

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
}
