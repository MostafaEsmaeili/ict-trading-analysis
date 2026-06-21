using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>Which aggregate operation an <see cref="ExitAction"/> tells the caller to apply (plan §2.5.9).</summary>
public enum ExitActionKind
{
    /// <summary>Take a T1 partial scale-out — <see cref="PaperTrade.ScaleOut"/>.</summary>
    ScaleOut,

    /// <summary>Ratchet the stop — <see cref="PaperTrade.MoveStop"/>.</summary>
    MoveStop,

    /// <summary>Close the (remaining) position — <see cref="PaperTrade.Close"/>.</summary>
    Close,
}

/// <summary>
/// One decided exit operation (plan §2.5.9) — the immutable DECIDE record the <see cref="IExitManager"/> emits and a
/// caller applies verbatim to the <see cref="PaperTrade"/>. The orchestrator pre-computes the §5.4 leg
/// <see cref="Costs"/> and stamps <see cref="AtUtc"/> (the bar-close time), so the caller's apply loop carries no
/// business logic. <see cref="Price"/> is the scale/close exit LEVEL, or the new stop for a <see cref="ExitActionKind.MoveStop"/>.
/// </summary>
public readonly record struct ExitAction
{
    private ExitAction(
        ExitActionKind kind,
        Price price,
        PositionSize? legSize,
        TradeCosts costs,
        TradeCloseReason? reason,
        DateTimeOffset atUtc)
    {
        Kind = kind;
        Price = price;
        LegSize = legSize;
        Costs = costs;
        Reason = reason;
        AtUtc = atUtc;
    }

    /// <summary>A T1 partial of <paramref name="legSize"/> lots at the <paramref name="level"/>, costing <paramref name="costs"/>.</summary>
    public static ExitAction ScaleOut(
        Price level, PositionSize legSize, TradeCosts costs, TradeCloseReason reason, DateTimeOffset atUtc)
        => new(ExitActionKind.ScaleOut, level, legSize, costs, reason, atUtc);

    /// <summary>A stop ratchet to <paramref name="newStop"/> (no money, no reason).</summary>
    public static ExitAction MoveStop(Price newStop, DateTimeOffset atUtc)
        => new(ExitActionKind.MoveStop, newStop, null, TradeCosts.Zero, null, atUtc);

    /// <summary>A close of the remaining position at the <paramref name="level"/>, costing <paramref name="costs"/>.</summary>
    public static ExitAction Close(Price level, TradeCloseReason reason, TradeCosts costs, DateTimeOffset atUtc)
        => new(ExitActionKind.Close, level, null, costs, reason, atUtc);

    public ExitActionKind Kind { get; }

    public Price Price { get; }

    /// <summary>The leg size — set on <see cref="ExitActionKind.ScaleOut"/> only; null otherwise.</summary>
    public PositionSize? LegSize { get; }

    /// <summary>The §5.4 leg costs (zero for a <see cref="ExitActionKind.MoveStop"/>).</summary>
    public TradeCosts Costs { get; }

    /// <summary>The close reason — set on scale/close; null for a <see cref="ExitActionKind.MoveStop"/>.</summary>
    public TradeCloseReason? Reason { get; }

    /// <summary>The bar-close time the action is stamped at.</summary>
    public DateTimeOffset AtUtc { get; }
}
