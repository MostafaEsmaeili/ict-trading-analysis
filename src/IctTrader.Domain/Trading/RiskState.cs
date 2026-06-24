using IctTrader.Domain.Common;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// An immutable snapshot of a <see cref="PaperAccount"/>'s adaptive-risk state (plan §2.4/§2.5.5) — everything the
/// pure <see cref="IRiskManager"/> needs to pick the effective per-trade risk: the consecutive win/loss streaks (the
/// loss-ladder and 5-win-cycle drivers) and the equity high-watermark + drawdown trough (the "restore after recovering
/// 50% of the loss" driver). It is produced BY the account (the single win/loss boundary) and consumed by the risk
/// manager, mirroring how <see cref="PositionSizing"/> is a pure DECIDE input — the account holds the authoritative
/// mutable state, this is a read-only view of it.
/// </summary>
public readonly record struct RiskState
{
    public RiskState(
        int consecutiveWins,
        int consecutiveLosses,
        Money currentEquity,
        Money peakEquity,
        Money dipTrough)
    {
        Guard.Against(consecutiveWins < 0, "ConsecutiveWins cannot be negative.");
        Guard.Against(consecutiveLosses < 0, "ConsecutiveLosses cannot be negative.");
        Guard.Against(peakEquity < dipTrough, "Peak equity cannot sit below the drawdown trough.");

        ConsecutiveWins = consecutiveWins;
        ConsecutiveLosses = consecutiveLosses;
        CurrentEquity = currentEquity;
        PeakEquity = peakEquity;
        DipTrough = dipTrough;
    }

    /// <summary>Consecutive winning settlements (resets to 0 on a loss or breakeven) — the 5-win-cycle driver.</summary>
    public int ConsecutiveWins { get; }

    /// <summary>Consecutive losing settlements (resets to 0 on a win) — the loss-ladder step driver.</summary>
    public int ConsecutiveLosses { get; }

    /// <summary>The account's current equity.</summary>
    public Money CurrentEquity { get; }

    /// <summary>The all-time equity high-watermark (the top of the current drawdown, if any).</summary>
    public Money PeakEquity { get; }

    /// <summary>The lowest equity since the peak — the bottom of the current drawdown.</summary>
    public Money DipTrough { get; }

    /// <summary>The opening state of a fresh account: no streaks, peak == trough == starting equity.</summary>
    public static RiskState Initial(Money startingEquity) =>
        new(0, 0, startingEquity, startingEquity, startingEquity);
}
