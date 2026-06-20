using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Anchors the dealing range the premium/discount read is measured against (plan §2.5.1 step 1, §2.5.10 —
/// bias verdict fix 1). NON-scoring (no <see cref="ConfluenceCondition"/>): it only maintains
/// <see cref="MarketContext.DailyRange"/> from the active swing structure — the lowest active swing-low to the
/// highest active swing-high — re-anchoring (expanding) when a new swing breaks beyond the current range. The
/// precise body-to-body broken-daily-swing anchoring (§2.5.10) is a documented refinement (spec §5 open item 6).
/// </summary>
public sealed class DealingRangeContextDetector : ISetupDetector
{
    private readonly PremiumDiscountOptions _options;

    public DealingRangeContextDetector(PremiumDiscountOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => null;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        decimal? high = null;
        decimal? low = null;
        foreach (var swing in context.SwingPoints)
        {
            if (!swing.IsActive)
            {
                continue;
            }

            if (swing.Kind == SwingKind.High)
            {
                high = high is { } h ? Math.Max(h, swing.Price.Value) : swing.Price.Value;
            }
            else
            {
                low = low is { } l ? Math.Min(l, swing.Price.Value) : swing.Price.Value;
            }
        }

        if (high is not { } rangeHigh || low is not { } rangeLow || rangeHigh <= rangeLow)
        {
            return DetectorResult.NoMatch; // not enough structure to frame a range
        }

        if (context.DailyRange is not { } existing)
        {
            context.SetDailyRange(new DealingRange(new Price(rangeLow), new Price(rangeHigh), current.OpenTimeUtc));
        }
        else if (rangeLow < existing.Low.Value || rangeHigh > existing.High.Value)
        {
            existing.Reanchor(
                new Price(Math.Min(rangeLow, existing.Low.Value)),
                new Price(Math.Max(rangeHigh, existing.High.Value)),
                current.OpenTimeUtc);
        }

        return DetectorResult.NoMatch; // context provider only
    }
}
