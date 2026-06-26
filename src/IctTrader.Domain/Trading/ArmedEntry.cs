using IctTrader.Domain.Common;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>The lifecycle of a resting entry limit (plan §2.5.1 step 7).</summary>
public enum ArmedEntryStatus
{
    /// <summary>The limit is resting — its risk reserved against the portfolio cap — waiting for the price retrace.</summary>
    Armed,

    /// <summary>The limit filled and produced an open <see cref="PaperTrade"/> under the same id (a key re-label).</summary>
    Triggered,

    /// <summary>The limit was cancelled unfilled (no-chase) — its reservation released back to the portfolio cap.</summary>
    Cancelled,
}

/// <summary>
/// A confirmed advisory <see cref="Setup"/> ARMED as a resting limit at the §2.5.1-step-7 OTE/FVG entry: the
/// position is sized ONCE at arm time and its risk reserved against the <see cref="PaperAccount"/>'s portfolio cap
/// (a resting limit is committed exposure, §2.5.10), waiting for price to retrace into the entry. Its identity IS
/// the future trade id, so when the entry touch triggers it, <see cref="PaperTradeFactory.OpenArmed"/> opens the
/// <see cref="PaperTrade"/> under the SAME id WITHOUT re-reserving (the trigger is a key re-label), and the account's
/// existing <see cref="PaperAccount.Settle"/> releases the reservation exactly as for an immediately-opened trade.
/// Paper only — it routes nothing (§6.3). The per-candle no-chase cancellation and the same-bar entry-then-stop −1R
/// straddle are decided by the orchestrator (cut 2b); this entity is the resting-order MECHANISM it drives.
/// </summary>
public sealed class ArmedEntry : AggregateRoot<Guid>
{
    /// <summary>
    /// EF Core materialization constructor — private so domain consumers cannot call it and bypass the
    /// public constructor's invariant guards. EF sets <see cref="AggregateRoot{TId}.Id"/> and all mapped
    /// fields via backing-field access after construction (plan §7 persistence conventions). No domain
    /// invariants are weakened: this ctor is inaccessible outside the ORM and outside this assembly.
    /// </summary>
#pragma warning disable CS8618 // EF sets fields after construction via PropertyAccessMode.Field.
    private ArmedEntry()
        : base(Guid.Empty)
    {
    }
#pragma warning restore CS8618

    public ArmedEntry(
        Guid id,
        Guid accountId,
        Setup setup,
        PositionSize size,
        Money riskBudget,
        decimal pipSize,
        decimal valuePerPip,
        InstrumentClass instrumentClass,
        DateTimeOffset armedAtUtc,
        decimal? stackedFartherBound = null)
        : base(id)
    {
        Guard.Against(id == Guid.Empty, "ArmedEntry requires a non-empty id.");
        Guard.Against(accountId == Guid.Empty, "ArmedEntry requires the owning account id.");
        ArgumentNullException.ThrowIfNull(setup);
        Guard.Against(!riskBudget.IsPositive, "ArmedEntry requires a positive reserved risk budget.");
        Guard.Against(pipSize <= 0m, "ArmedEntry requires a positive pip size.");
        Guard.Against(valuePerPip <= 0m, "ArmedEntry requires a positive value-per-pip.");
        Guard.Against(armedAtUtc.Offset != TimeSpan.Zero, "ArmedEntry.ArmedAtUtc must be UTC.");

        AccountId = accountId;
        Setup = setup;
        Size = size;
        RiskBudget = riskBudget;
        PipSize = pipSize;
        ValuePerPip = valuePerPip;
        InstrumentClass = instrumentClass;
        ArmedAtUtc = armedAtUtc;
        StackedFartherBound = stackedFartherBound;
        Status = ArmedEntryStatus.Armed;

        RaiseDomainEvent(new EntryArmed(Id, AccountId, Symbol, Direction, Size, RiskBudget, armedAtUtc));
    }

    public Guid AccountId { get; }

    /// <summary>The confirmed advisory setup whose plan entry the limit rests at.</summary>
    public Setup Setup { get; }

    /// <summary>The position sized at arm time — carried verbatim to the opened trade so the trade's derived
    /// <see cref="PaperTrade.RiskBudget"/> equals the reserved <see cref="RiskBudget"/> (no drift across the handoff).</summary>
    public PositionSize Size { get; }

    /// <summary>The money reserved against the account's portfolio cap while the limit rests (== the eventual trade's
    /// <see cref="PaperTrade.RiskBudget"/>).</summary>
    public Money RiskBudget { get; }

    /// <summary>The instrument's pip size — carried so the orchestrator can build the would-be trade and
    /// <see cref="PaperTradeFactory.OpenArmed"/> opens at the same money geometry it was sized with.</summary>
    public decimal PipSize { get; }

    /// <summary>The instrument's value-per-pip per lot — carried for the same reason as <see cref="PipSize"/>.</summary>
    public decimal ValuePerPip { get; }

    /// <summary>The instrument class — carried so the orchestrator can classify the killzone for the no-chase rung.</summary>
    public InstrumentClass InstrumentClass { get; }

    /// <summary>
    /// FVG-SEM-2b: the far-edge of the deeper stacked FVG (Ep3 L376-413), or null when the entry was not stacked. The
    /// EntryManager's wrong-order NIX cancels the resting limit if a retrace reaches this bound before the limit fills
    /// (a stab into the farther gap first = no-trade). The stop already clears it (the §1 widened stop on the Setup's
    /// plan), so a POST-fill touch is the stop's job — the NIX is pre-fill only.
    /// </summary>
    public decimal? StackedFartherBound { get; }

    /// <summary>True when the resting limit sits in front of a stacked farther gap — the wrong-order NIX is armed.</summary>
    public bool IsStacked => StackedFartherBound is not null;

    public DateTimeOffset ArmedAtUtc { get; }

    public ArmedEntryStatus Status { get; private set; }

    public Symbol Symbol => Setup.Symbol;

    public Direction Direction => Setup.Direction;

    /// <summary>Marks the resting limit filled at <paramref name="triggeredAtUtc"/> — its reservation carries to the
    /// produced trade's (identical) id. Legal only once from <see cref="ArmedEntryStatus.Armed"/>, and not before the
    /// arm time (the timeline stays monotonic). Clock-free: the caller passes the fill bar-close time.</summary>
    public void MarkTriggered(DateTimeOffset triggeredAtUtc)
    {
        Guard.Against(Status != ArmedEntryStatus.Armed, "Only an armed entry can be triggered.");
        Guard.Against(triggeredAtUtc.Offset != TimeSpan.Zero, "ArmedEntry trigger time must be UTC.");
        Guard.Against(triggeredAtUtc < ArmedAtUtc, "An armed entry cannot trigger before it was armed.");

        Status = ArmedEntryStatus.Triggered;
        RaiseDomainEvent(new EntryTriggered(Id, AccountId, triggeredAtUtc));
    }

    /// <summary>Cancels the resting limit unfilled at <paramref name="atUtc"/> for the no-chase
    /// <paramref name="reason"/> (plan §2.5.1 "don't chase"). Legal only once from <see cref="ArmedEntryStatus.Armed"/>,
    /// and not before the arm time. The caller releases the reservation on the account (<see cref="PaperAccount.Release"/>)
    /// so the cap self-heals. Clock-free: the caller passes the bar-close time.</summary>
    public void Cancel(EntryCancelReason reason, DateTimeOffset atUtc)
    {
        Guard.Against(Status != ArmedEntryStatus.Armed, "Only an armed entry can be cancelled.");
        Guard.Against(atUtc.Offset != TimeSpan.Zero, "ArmedEntry cancel time must be UTC.");
        Guard.Against(atUtc < ArmedAtUtc, "An armed entry cannot be cancelled before it was armed.");

        Status = ArmedEntryStatus.Cancelled;
        RaiseDomainEvent(new EntryCancelled(Id, AccountId, reason, atUtc));
    }
}
