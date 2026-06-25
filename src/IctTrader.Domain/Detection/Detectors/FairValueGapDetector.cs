using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects 3-candle fair-value gaps (plan §2.3/§2.5.1 step 6) and maintains their lifecycle. Bullish when
/// c1.High &lt; c3.Low, bearish when c1.Low &gt; c3.High. It emits <see cref="ConfluenceCondition.FvgPresent"/>
/// only when the gap sits in the CORRECT premium/discount half of the displacement leg:
/// <b>bullish/long ⇒ discount (gap top ≤ equilibrium); bearish/short ⇒ premium (gap bottom ≥ equilibrium)</b>
/// (the verifier-corrected operators — never buy in premium / sell in discount). Open gaps two-touch-void
/// on the configured retrace count and die when fully mitigated.
/// </summary>
public sealed class FairValueGapDetector : ISetupDetector
{
    private readonly FvgOptions _options;

    public FairValueGapDetector(FvgOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.FvgPresent;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        UpdateOpenGaps(context, current);
        return DetectFormation(context, current);
    }

    private void UpdateOpenGaps(MarketContext context, Candle current)
    {
        foreach (var gap in context.OpenFvgs)
        {
            if (!gap.IsOpen)
            {
                continue;
            }

            var fullyFilled = gap.Direction == Direction.Bullish
                ? current.Low <= gap.Bottom.Value
                : current.High >= gap.Top.Value;

            if (_options.MitigateOnFullFill && fullyFilled)
            {
                gap.Mitigate();
                continue;
            }

            // FVG-SEM-1a: a return into the void counts as a touch by the configured semantics — wick-into (any bar
            // whose range trades into the gap, the Ep38 default) or close-into (only a bar that closes inside it).
            var touched = _options.TouchSemantics == FvgTouchSemantics.CloseInto
                ? current.Close >= gap.Bottom.Value && current.Close <= gap.Top.Value
                : current.Low <= gap.Top.Value && current.High >= gap.Bottom.Value;
            if (touched)
            {
                gap.RegisterTouch(_options.VoidOnTouchCount);
            }
        }
    }

    private DetectorResult DetectFormation(MarketContext context, Candle current)
    {
        var window = context.Window(current.Timeframe);
        if (window.Count < 3)
        {
            return DetectorResult.NoMatch;
        }

        var c1 = window[^3];
        var middle = window[^2];
        var c3 = window[^1];

        Direction direction;
        decimal bottom;
        decimal top;

        if (c1.High < c3.Low)
        {
            (direction, bottom, top) = (Direction.Bullish, c1.High, c3.Low);
        }
        else if (c1.Low > c3.High)
        {
            (direction, bottom, top) = (Direction.Bearish, c3.High, c1.Low);
        }
        else
        {
            return DetectorResult.NoMatch;
        }

        var gap = new FairValueGap(direction, current.Timeframe, new Price(bottom), new Price(top), middle.OpenTimeUtc);
        var inCorrectHalf = EvaluateCorrectHalf(context, direction, gap);
        context.RegisterFvg(gap);

        if (_options.RequireInCorrectHalf && !inCorrectHalf)
        {
            return DetectorResult.NoMatch;
        }

        // FVG-SEM-3: compute the five validity-exclusion predicates (§2.5.10). They are attached as flag-only
        // evidence UNCONDITIONALLY below; only Asian-range / overlapping-wicks veto, and only when the flag is on.
        var excludeNoSweep = ComputeExcludeNoSweep(context, direction, gap.FormedAtUtc);
        var excludeAsianRange = context.Session.Killzone == Killzone.Asian;
        var excludeCounterBias = context.Bias is not { } bias || bias != direction;
        var excludeNoChoch = context.LastMss is not { } mss || mss.Direction != direction || !mss.IsConfirmed;
        var excludeOverlappingWicks = direction == Direction.Bullish ? c1.High >= c3.Low : c1.Low <= c3.High;
        var anyExclusion =
            excludeNoSweep || excludeAsianRange || excludeCounterBias || excludeNoChoch || excludeOverlappingWicks;

        // The veto (FVG-SEM-3 ON) fires ONLY on the two FSM-unowned exclusions, inserted AFTER the correct-half
        // early-return so the OFF path is byte-unchanged. No-sweep/counter-bias/no-CHoCH are already FSM
        // RequiredConditions, so vetoing them here would double-enforce/desync — they annotate but never veto.
        if (_options.ApplyValidityExclusions && (excludeAsianRange || excludeOverlappingWicks))
        {
            return DetectorResult.NoMatch;
        }

        var keyLevel = direction == Direction.Bullish ? top : bottom;
        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.GapBottomPrice] = bottom,
            [EvidenceKeys.GapTopPrice] = top,
            [EvidenceKeys.Timeframe] = current.Timeframe.ToString(),
            [EvidenceKeys.Direction] = direction.ToString(),
            [EvidenceKeys.InCorrectHalf] = inCorrectHalf,
            [EvidenceKeys.ExcludedNoSweep] = excludeNoSweep,
            [EvidenceKeys.ExcludedAsianRange] = excludeAsianRange,
            [EvidenceKeys.ExcludedCounterBias] = excludeCounterBias,
            [EvidenceKeys.ExcludedNoChoch] = excludeNoChoch,
            [EvidenceKeys.ExcludedOverlappingWicks] = excludeOverlappingWicks,
            [EvidenceKeys.AnyValidityExclusion] = anyExclusion,
        };

        return DetectorResult.Match(
            direction, keyLevel, ReasonFragments.FvgFormed(direction, bottom, top, current.Timeframe), evidence);
    }

    // FVG-SEM-3 (a): EXCLUDED when there is no precedent sweep in the gap's direction strictly before it formed.
    // SweepRecord.Direction is already the enabled trade direction; the strict precede mirrors the FSM
    // sweep-must-precede rule (a sweep landing at/after the gap's formation is a future event, not its premise).
    private static bool ComputeExcludeNoSweep(MarketContext context, Direction direction, DateTimeOffset formedAtUtc)
        => context.LastSweep is not { } sweep || sweep.Direction != direction || sweep.AtUtc >= formedAtUtc;

    private bool EvaluateCorrectHalf(MarketContext context, Direction direction, FairValueGap gap)
    {
        if (!_options.RequireInCorrectHalf)
        {
            return true;
        }

        var equilibrium = context.LastDisplacement?.EquilibriumPrice;
        if (equilibrium is null)
        {
            return false;
        }

        // Long/bullish FVG must be in DISCOUNT (gap top at/below equilibrium); short/bearish in PREMIUM
        // (gap bottom at/above equilibrium). These are the verifier-corrected operators.
        return direction == Direction.Bullish
            ? gap.Top.Value <= equilibrium.Value
            : gap.Bottom.Value >= equilibrium.Value;
    }
}
