using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects fractal swing points (plan §2.3/§2.5.1 step 5) and feeds the swing registry that sweep, MSS,
/// and stop-placement read. A feeder — it carries no <see cref="ConfluenceCondition"/> and does not score.
/// With <see cref="SwingOptions.StrictInequality"/> on, equal highs/lows are deliberately NOT swings (they
/// are liquidity). Invalidation: a swing whose level is CLOSED beyond is breached (ITH/ITL), distinct from
/// a wick that merely sweeps it.
/// </summary>
public sealed class SwingPointDetector : ISetupDetector
{
    private readonly SwingOptions _options;

    public SwingPointDetector(SwingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => null;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_options.InvalidateOnCloseBeyond)
        {
            BreachClosedSwings(context, current);
        }

        var window = context.Window(current.Timeframe);
        if (window.Count < _options.FractalWidth)
        {
            return DetectorResult.NoMatch;
        }

        var half = (_options.FractalWidth - 1) / 2;
        var pivotIndex = window.Count - 1 - half;
        var pivot = window[pivotIndex];

        var (isHigh, isLow) = EvaluatePivot(window, pivotIndex, half);

        if (isHigh)
        {
            return RegisterSwing(context, SwingKind.High, pivot.High, pivot, current.Timeframe);
        }

        return isLow
            ? RegisterSwing(context, SwingKind.Low, pivot.Low, pivot, current.Timeframe)
            : DetectorResult.NoMatch;
    }

    private (bool IsHigh, bool IsLow) EvaluatePivot(IReadOnlyList<Candle> window, int pivotIndex, int half)
    {
        var pivot = window[pivotIndex];
        var isHigh = true;
        var isLow = true;

        for (var i = pivotIndex - half; i <= pivotIndex + half; i++)
        {
            if (i == pivotIndex)
            {
                continue;
            }

            var neighbour = window[i];
            if (_options.StrictInequality)
            {
                isHigh &= neighbour.High < pivot.High;
                isLow &= neighbour.Low > pivot.Low;
            }
            else
            {
                isHigh &= neighbour.High <= pivot.High;
                isLow &= neighbour.Low >= pivot.Low;
            }
        }

        return (isHigh, isLow);
    }

    private static DetectorResult RegisterSwing(
        MarketContext context, SwingKind kind, decimal price, Candle pivot, Timeframe timeframe)
    {
        var swing = new SwingPoint(kind, timeframe, new Price(price), pivot.OpenTimeUtc);
        context.RegisterSwingPoint(swing);

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.SwingPrice] = price,
            [EvidenceKeys.Timeframe] = timeframe.ToString(),
            [EvidenceKeys.Direction] = swing.EnablesDirection.ToString(),
        };

        return DetectorResult.Match(
            swing.EnablesDirection,
            price,
            ReasonFragments.SwingFormed(kind, price, timeframe),
            evidence);
    }

    private static void BreachClosedSwings(MarketContext context, Candle current)
    {
        foreach (var swing in context.SwingPoints)
        {
            if (!swing.IsActive)
            {
                continue;
            }

            var brokenHigh = swing.Kind == SwingKind.High && current.Close > swing.Price.Value;
            var brokenLow = swing.Kind == SwingKind.Low && current.Close < swing.Price.Value;
            if (brokenHigh || brokenLow)
            {
                swing.Breach();
            }
        }
    }
}
