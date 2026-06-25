using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects an energetic displacement candle (plan §2.5.1 step 5, §2.5.7 caveat 5) and publishes the leg
/// whose 50% the FVG/OTE premium-discount gate reads. A NON-scoring precondition — it carries no
/// <see cref="ConfluenceCondition"/>; the single <c>DisplacementMss</c> weight is owned by the MSS detector.
/// Energy is quantified as body-to-range ≥ floor AND body ≥ ATR×multiple (an absolute pip floor is OFF by
/// default so it is never a silent extra filter). Invalidation: the prior leg is marked retraced when price
/// closes back beyond its origin.
/// </summary>
public sealed class DisplacementDetector : ISetupDetector
{
    private readonly DisplacementOptions _options;

    public DisplacementDetector(DisplacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => null;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        InvalidateRetracedLeg(context, current);

        var window = context.Window(current.Timeframe);
        if (window.Count < _options.AtrPeriod + 1)
        {
            return DetectorResult.NoMatch; // ATR warmup
        }

        var range = current.High - current.Low;
        if (range <= 0m)
        {
            return DetectorResult.NoMatch;
        }

        // Direction seed: an inside/doji candle is never a leg terminus (the per-candle terminus precondition).
        if (current.Close == current.Open)
        {
            return DetectorResult.NoMatch;
        }

        var direction = current.Close > current.Open ? Direction.Bullish : Direction.Bearish;

        // EG-1: resolve the anchor mode ONCE, leg-wide, BEFORE growing — so the extension metric and the boundary
        // anchor never disagree. Wick-to-wick on the FOMC/NFP exception or operator override.
        var anchor = ResolveAnchor(context);

        // Grow the run backward from the terminus (TIME-11-12): absorb each older same-direction candle whose
        // anchored extreme strictly extends the run, capped to the last DisplacementLegMaxBars candles.
        var lastIdx = window.Count - 1;
        var startIdx = lastIdx;
        var runExtreme = Extreme(window[lastIdx], direction, anchor);
        var floor = Math.Max(0, lastIdx - (_options.DisplacementLegMaxBars - 1));
        for (var i = lastIdx - 1; i >= floor; i--)
        {
            var c = window[i];
            var sameDir = direction == Direction.Bullish ? c.Close > c.Open : c.Close < c.Open;
            if (!sameDir)
            {
                break; // a counter / doji candle ends the run
            }

            var cExtreme = Extreme(c, direction, anchor);
            var extends = direction == Direction.Bullish ? cExtreme < runExtreme : cExtreme > runExtreme;
            if (!extends)
            {
                break; // a stall / overlap ends the run (strict so an equal-extreme never glues a candle on)
            }

            startIdx = i;
            runExtreme = cExtreme;
        }

        var legBars = lastIdx - startIdx + 1;
        var originCandle = window[startIdx];
        var terminusCandle = current; // window[lastIdx] == current

        // EG-1 boundary-candle anchors: min/max(Open,Close) of the BOUNDARY candles (NOT literal first-Open/last-Close).
        var (origin, terminus) = anchor == LegAnchorMode.WickToWick
            ? direction == Direction.Bullish
                ? (originCandle.Low, terminusCandle.High)
                : (originCandle.High, terminusCandle.Low)
            : direction == Direction.Bullish
                ? (Math.Min(originCandle.Open, originCandle.Close), Math.Max(terminusCandle.Open, terminusCandle.Close))
                : (Math.Max(originCandle.Open, originCandle.Close), Math.Min(terminusCandle.Open, terminusCandle.Close));

        // Aggregate energy gate over the assembled run (reduces to the per-candle gate at length 1): the net
        // directional thrust must dominate the leg range AND clear ATR×multiple.
        var legRange = MaxHigh(window, startIdx, lastIdx) - MinLow(window, startIdx, lastIdx);
        if (legRange <= 0m)
        {
            return DetectorResult.NoMatch;
        }

        var legBody = Math.Abs(terminus - origin);
        var atr = AverageTrueRange(window, _options.AtrPeriod);
        var energetic = legBody / legRange >= _options.MinBodyToRangeRatio && legBody >= _options.AtrMultiple * atr;
        if (_options.MinDisplacementPips > 0m)
        {
            energetic &= context.SymbolSpec.PriceToPips(legBody).Value >= _options.MinDisplacementPips;
        }

        if (!energetic)
        {
            return DetectorResult.NoMatch; // weak / anemic expansion
        }

        var leg = new Displacement(
            direction,
            current.Timeframe,
            new Price(origin),
            new Price(terminus),
            current.OpenTimeUtc,
            originCandle.OpenTimeUtc,
            legBars);
        context.SetDisplacement(leg);

        var pips = context.SymbolSpec.PriceToPips(legBody).Value;
        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = direction.ToString(),
            [EvidenceKeys.DisplacementPips] = pips,
            [EvidenceKeys.DisplacementLegBars] = legBars,
            [EvidenceKeys.Timeframe] = current.Timeframe.ToString(),
        };

        return DetectorResult.Match(
            direction, terminus, ReasonFragments.Displacement(direction, pips, current.Timeframe), evidence);
    }

    // The anchored extreme used both to grow the run and to compute the boundary anchors.
    private static decimal Extreme(Candle candle, Direction direction, LegAnchorMode anchor)
        => anchor == LegAnchorMode.WickToWick
            ? direction == Direction.Bullish ? candle.High : candle.Low
            : direction == Direction.Bullish ? Math.Max(candle.Open, candle.Close) : Math.Min(candle.Open, candle.Close);

    private static decimal MaxHigh(IReadOnlyList<Candle> window, int startIdx, int lastIdx)
    {
        var max = window[startIdx].High;
        for (var i = startIdx + 1; i <= lastIdx; i++)
        {
            max = Math.Max(max, window[i].High);
        }

        return max;
    }

    private static decimal MinLow(IReadOnlyList<Candle> window, int startIdx, int lastIdx)
    {
        var min = window[startIdx].Low;
        for (var i = startIdx + 1; i <= lastIdx; i++)
        {
            min = Math.Min(min, window[i].Low);
        }

        return min;
    }

    private static void InvalidateRetracedLeg(MarketContext context, Candle current)
    {
        var leg = context.LastDisplacement;
        if (leg is null || leg.Retraced)
        {
            return;
        }

        var retraced = leg.Direction == Direction.Bullish
            ? current.Close < leg.Origin.Value
            : current.Close > leg.Origin.Value;

        if (retraced)
        {
            leg.MarkRetraced();
        }
    }

    private LegAnchorMode ResolveAnchor(MarketContext context)
    {
        if (_options.AnchorMode == LegAnchorMode.WickToWick)
        {
            return LegAnchorMode.WickToWick; // operator override
        }

        if (_options.WickAnchorOnFomcNfp && IsFomcOrNfpDay(context))
        {
            return LegAnchorMode.WickToWick; // ICT FOMC/NFP exception — use the extremes on high-volatility days
        }

        return LegAnchorMode.BodyToBody; // the §2.5.10 faithful default (also the fail-open path)
    }

    private static bool IsFomcOrNfpDay(MarketContext context)
    {
        // Fail-open to body when there is no NY date yet or no calendar loaded (mirrors CalendarGateDetector).
        if (context.CurrentNewYorkDate is not { } date || !context.IsCalendarLoaded)
        {
            return false;
        }

        foreach (var economicEvent in context.EconomicEvents)
        {
            if (economicEvent.NyDate == date
                && economicEvent.Type is CalendarEventType.Fomc or CalendarEventType.Nfp)
            {
                return true;
            }
        }

        return false;
    }

    private static decimal AverageTrueRange(IReadOnlyList<Candle> window, int period)
    {
        var sum = 0m;
        for (var i = window.Count - period; i < window.Count; i++)
        {
            var candle = window[i];
            var previousClose = window[i - 1].Close;
            var trueRange = Math.Max(
                candle.High - candle.Low,
                Math.Max(Math.Abs(candle.High - previousClose), Math.Abs(candle.Low - previousClose)));
            sum += trueRange;
        }

        return sum / period;
    }
}
