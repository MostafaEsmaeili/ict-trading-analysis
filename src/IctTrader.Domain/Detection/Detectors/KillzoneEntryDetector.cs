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
    private readonly SilverBulletOptions _silverBullet;

    public KillzoneEntryDetector(KillzoneEntryOptions options, SilverBulletOptions? silverBullet = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        // The Silver-Bullet macro overlay is OPTIONAL: when unwired (or disabled) the gate is byte-identical to today.
        _silverBullet = silverBullet ?? new SilverBulletOptions();
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

        // Silver-Bullet macro overlay (opt-in): NARROW the already-active killzone to an enabled macro window (an
        // INTERSECTION, never a widening — the killzone check above still gates). For an index this trims IndexAm to
        // the macro; for FX it trims LondonClose. Off by default → no-op. Adds no ConfluenceCondition weight (Σ=9.75).
        if (_silverBullet.Enabled)
        {
            var nyTime = context.NewYorkTimeOfDay(current.OpenTimeUtc);
            if (!_silverBullet.ContainsMacro(nyTime))
            {
                return DetectorResult.NoMatch; // in an active killzone, but OUTSIDE every enabled Silver-Bullet macro
            }

            evidence[EvidenceKeys.SilverBulletMacro] = nyTime.ToString("HH:mm");
        }

        return DetectorResult.Match(
            direction: null, keyLevel: null, ReasonFragments.KillzoneEntry(session.Killzone), evidence);
    }
}
