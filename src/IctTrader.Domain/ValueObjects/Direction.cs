namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// Market direction of a structure, bias, or setup (plan §2.3). FROZEN CONTRACT (plan §11.1): member
/// names are depended on by DTOs and the dashboard.
/// </summary>
public enum Direction
{
    Bullish,
    Bearish,
}

/// <summary>The side of a (paper) trade. FROZEN CONTRACT (plan §11.1): Gherkin asserts "Long"/"Short".</summary>
public enum TradeDirection
{
    Long,
    Short,
}

public static class DirectionExtensions
{
    public static Direction Opposite(this Direction direction)
        => direction == Direction.Bullish ? Direction.Bearish : Direction.Bullish;

    public static TradeDirection ToTradeDirection(this Direction direction)
        => direction == Direction.Bullish ? TradeDirection.Long : TradeDirection.Short;
}
