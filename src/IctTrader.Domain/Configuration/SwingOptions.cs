namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable swing-detection parameters (plan §2.5.1 step 5). The fractal width defaults to a 3-candle swing
/// (one candle either side); <see cref="StrictInequality"/> means equal highs/lows are treated as
/// LIQUIDITY, not swings. Bound from <c>Ict:Detection:Swing</c>.
/// </summary>
public sealed class SwingOptions
{
    public const string SectionName = "Ict:Detection:Swing";

    /// <summary>Odd number of candles forming a fractal; 3 = one candle either side (open: 3 vs 5, §spec).</summary>
    public int FractalWidth { get; init; } = 3;

    public bool StrictInequality { get; init; } = true;

    /// <summary>Invalidate a swing when price CLOSES beyond it (ITH/ITL breach), distinct from a wick sweep.</summary>
    public bool InvalidateOnCloseBeyond { get; init; } = true;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (FractalWidth < 3)
        {
            errors.Add($"Swing FractalWidth must be at least 3 but was {FractalWidth}.");
        }

        if (FractalWidth % 2 == 0)
        {
            errors.Add($"Swing FractalWidth must be odd (a centred pivot) but was {FractalWidth}.");
        }

        return errors;
    }
}
