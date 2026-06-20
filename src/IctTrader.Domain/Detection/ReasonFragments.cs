using System.Globalization;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection;

/// <summary>
/// Central, deterministic templates for the human-readable clause each detector contributes to a
/// <see cref="DetectorResult.ReasonFragment"/> (plan §4.5). Centralised here so the detector logic carries
/// no inline literals; the full localisable <c>.resx</c> migration is a later (Host/Resources) WP. Numbers
/// are formatted with the invariant culture so the text is identical on any host.
/// </summary>
public static class ReasonFragments
{
    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static string SwingFormed(SwingKind kind, decimal price, Timeframe timeframe)
        => $"Swing {kind.ToString().ToLowerInvariant()} formed at {N(price)} on {timeframe}";

    public static string FvgFormed(Direction direction, decimal bottom, decimal top, Timeframe timeframe)
        => $"{direction} FVG ({N(bottom)}-{N(top)}) on {timeframe}";

    public static string LiquiditySwept(LiquiditySide side, decimal level)
        => $"{side} liquidity swept at {N(level)}";

    public static string Displacement(Direction direction, decimal pips, Timeframe timeframe)
        => $"{direction} displacement of {N(pips)} pips on {timeframe}";

    public static string MarketStructureShift(Direction direction, decimal brokenLevel, Timeframe timeframe)
        => $"{direction} MSS, broke swing {N(brokenLevel)} on {timeframe}";

    public static string OrderBlock(Direction direction, decimal openingPrice, Timeframe timeframe)
        => $"{direction} order block at {N(openingPrice)} on {timeframe}";

    public static string KillzoneEntry(Killzone killzone)
        => $"Inside the {killzone} killzone";

    public static string DrawTarget(Direction direction, decimal targetLevel, decimal rewardRatio)
        => $"{direction} draw on liquidity at {N(targetLevel)} ({N(rewardRatio)}R)";

    public static string DailyBias(Direction direction, decimal positionPercent)
        => $"Daily bias {direction} at {N(positionPercent)}% of the dealing range";

    public static string PremiumDiscountHalf(PremiumDiscount half)
        => $"Price in the {half.ToString().ToLowerInvariant()} half of the dealing range";

    public static string OteEntry(Direction direction, decimal sweetSpot, Timeframe timeframe)
        => $"{direction} OTE entry, sweet spot {N(sweetSpot)} on {timeframe}";

    public static string CalendarClear(DateOnly nyDate)
        => $"Calendar clear for {nyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    public static string CalendarClearUnverified()
        => "Calendar clear (no calendar data loaded)";
}
