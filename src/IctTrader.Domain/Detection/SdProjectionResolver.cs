using IctTrader.Domain.Configuration;

namespace IctTrader.Domain.Detection;

/// <summary>
/// Resolves the standard-deviation projection targets for the current displacement leg (decision TGR-1/2). PURE and
/// NON-scoring: it reads ONLY <see cref="MarketContext.LastDisplacement"/> (TGR-2 single-source — it never re-derives
/// the leg), and prices each tier by projecting the leg magnitude beyond the terminus via the SAME axis the OTE entry
/// uses (<see cref="MarketStructure.Displacement.Project"/>), so the SD targets and the OTE entry can never drift.
/// </summary>
public static class SdProjectionResolver
{
    /// <summary>The SD projection on the current leg, or null when there is no leg, it fully retraced, or it is
    /// degenerate (zero length).</summary>
    public static SdProjection? Resolve(MarketContext context, SdProjectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (context.LastDisplacement is not { } leg || leg.Retraced)
        {
            return null; // no leg, or it fully retraced (mirrors OteEntryResolver)
        }

        var legLength = leg.Size;
        if (legLength == 0m)
        {
            return null; // a degenerate (zero-length) leg projects nothing
        }

        // The negative-fib variant (Primer-flagged opt-in) REPLACES the SD multiples on the SAME terminus axis.
        var multiples = options.EffectiveMultiples;
        var tiers = new List<SdTier>(multiples.Count);
        foreach (var multiple in multiples)
        {
            // Project beyond the terminus: leg.Project(-n) = Terminus + n × (Terminus - Origin) = the −n SD level.
            tiers.Add(new SdTier(multiple, leg.Project(-multiple)));
        }

        return new SdProjection(leg.Direction, legLength, leg.Terminus.Value, tiers);
    }
}
