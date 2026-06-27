using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Emits the §2.5.2 RequiredCondition <see cref="ConfluenceCondition.KillzoneEntry"/> (weight 1.0) — the time
/// gate of the entry model (plan §2.5.1 step 3, §4.6): a setup may only confirm inside an operator-enabled
/// killzone, never during the hard lunch window or past the index-AM entry cutoff. Non-directional (it gates
/// time, not direction), so the FSM counts it for either side. The session classification is computed once by
/// <see cref="MarketContext.Append"/>; this detector only reads it against the enabled set.
/// </summary>
public sealed class KillzoneEntryDetector : ISetupDetector
{
    private readonly KillzoneEntryOptions _options;

    public KillzoneEntryDetector(KillzoneEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.KillzoneEntry;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        var session = context.Session;
        if (!session.IsActiveEntryFor(context.InstrumentClass, _options.ResolvedActiveKillzones))
        {
            return DetectorResult.NoMatch;
        }

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Killzone] = session.Killzone.ToString(),
        };

        return DetectorResult.Match(
            direction: null, keyLevel: null, ReasonFragments.KillzoneEntry(session.Killzone), evidence);
    }
}
