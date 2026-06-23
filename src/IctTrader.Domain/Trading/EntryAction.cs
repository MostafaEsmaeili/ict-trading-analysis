using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>Which operation an <see cref="EntryAction"/> tells the caller to apply (plan §2.5.1 step 7).</summary>
public enum EntryActionKind
{
    /// <summary>Open the trade from the armed entry at the fill level — <see cref="PaperTradeFactory.OpenArmed"/>.</summary>
    Open,

    /// <summary>Close the just-opened trade (the same-bar entry-then-stop −1R straddle) — <see cref="PaperTrade.Close"/>.</summary>
    Close,
}

/// <summary>
/// One decided entry operation (plan §2.5.1 step 7) — the immutable DECIDE record the <see cref="IEntryManager"/>
/// emits and a caller applies. A clean fill is a single <see cref="EntryActionKind.Open"/>; a fast bar that fills the
/// limit AND runs to the stop is an apply-ordered <see cref="EntryActionKind.Open"/> then <see cref="EntryActionKind.Close"/>
/// (the −1R straddle), both stamped at the same bar-close time. The <see cref="Open"/> action books no cost (the entry
/// spread rides the eventual exit); the straddle <see cref="Close"/> carries the costed full round trip.
/// </summary>
public readonly record struct EntryAction
{
    private EntryAction(
        EntryActionKind kind, Price price, TradeCosts costs, TradeCloseReason? reason, DateTimeOffset atUtc)
    {
        Guard.Against(atUtc.Offset != TimeSpan.Zero, "EntryAction timestamps must be UTC.");
        Kind = kind;
        Price = price;
        Costs = costs;
        Reason = reason;
        AtUtc = atUtc;
    }

    /// <summary>Open the trade at the <paramref name="fillLevel"/> (the limit level). The caller applies it via
    /// <see cref="PaperTradeFactory.OpenArmed"/>; the entry spread is the deferred §5.4 cost line.</summary>
    public static EntryAction Open(Price fillLevel, DateTimeOffset atUtc)
        => new(EntryActionKind.Open, fillLevel, TradeCosts.Zero, null, atUtc);

    /// <summary>Close the just-opened trade at the <paramref name="level"/> (the same-bar straddle), costing the full
    /// round trip <paramref name="costs"/>.</summary>
    public static EntryAction Close(Price level, TradeCloseReason reason, TradeCosts costs, DateTimeOffset atUtc)
        => new(EntryActionKind.Close, level, costs, reason, atUtc);

    public EntryActionKind Kind { get; }

    public Price Price { get; }

    /// <summary>The §5.4 costs — the full round trip on a straddle <see cref="EntryActionKind.Close"/>, zero on an open.</summary>
    public TradeCosts Costs { get; }

    /// <summary>The close reason — set on a straddle <see cref="EntryActionKind.Close"/>; null on an open.</summary>
    public TradeCloseReason? Reason { get; }

    /// <summary>The bar-close time the action is stamped at.</summary>
    public DateTimeOffset AtUtc { get; }
}
