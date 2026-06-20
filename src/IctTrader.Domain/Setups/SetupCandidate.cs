using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Domain.Detection;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Setups;

/// <summary>
/// The per-symbol confluence FSM (plan §3.0/§4.4): it accumulates the matched <see cref="ConfluenceCondition"/>s
/// emitted by the detectors into a single, direction-locked setup and confirms a graded
/// <see cref="SetupConfirmation"/> when the §2.5 model is satisfied. A pure domain process — no I/O, no ambient
/// clock; <see cref="Observe"/> reads the per-symbol <see cref="MarketContext"/> and the candle's matches.
///
/// <para>Faithful to the mined entry model:</para>
/// <list type="bullet">
/// <item><b>The MSS owns the direction lock.</b> The locked trade direction is the direction of the latest
/// <see cref="ConfluenceCondition.DisplacementMss"/>; an opposing shift reseeds it (an intraday reversal is a
/// new setup, not a contradiction to ignore). Any condition whose emitted direction contradicts the lock is
/// simply not counted — that is how the premium/discount entry-half veto is realised.</item>
/// <item><b>Standing vs event conditions.</b> Standing filters (bias, premium/discount, killzone, calendar)
/// describe the CURRENT candle and are re-evaluated every candle, so a price crossing into the wrong half
/// withdraws its required match live. Event conditions (sweep, MSS, FVG, OB, OTE) latch on formation and age
/// out after <see cref="SetupCandidateOptions.MaxAssemblyBars"/>.</item>
/// <item><b>The sweep strictly precedes the MSS</b> (§2.5 steps 4 then 5): a sweep latched AFTER the shift does
/// not complete this setup.</item>
/// <item><b>Teardown on a broken premise:</b> the candidate dies if its anchoring MSS is invalidated
/// (ITH/ITL breach). <see cref="Reset"/> is also driven by the session on a NY-day rollover / killzone change.</item>
/// </list>
/// This models the Accumulating→Confirmed lifecycle; the post-confirmation Armed/Triggered entry states (price
/// reaching the OTE limit, the paper-trade handoff) belong with the fill simulator (WP4/WP5).
/// </summary>
public sealed class SetupCandidate
{
    private readonly record struct Latched(long BarIndex, DetectorResult Result);

    // The narrative order setup reasoning is rendered in (bias → killzone → sweep → MSS → FVG → ...).
    private static readonly IReadOnlyList<ConfluenceCondition> ReasonOrder =
    [
        ConfluenceCondition.BiasAligned,
        ConfluenceCondition.KillzoneEntry,
        ConfluenceCondition.LiquiditySweep,
        ConfluenceCondition.DisplacementMss,
        ConfluenceCondition.FvgPresent,
        ConfluenceCondition.OrderBlockConfluence,
        ConfluenceCondition.PremiumDiscountHalf,
        ConfluenceCondition.OteZone,
        ConfluenceCondition.DrawTargetRrMet,
        ConfluenceCondition.SmtDivergence,
        ConfluenceCondition.OpenPriceReference,
        ConfluenceCondition.MacroTime,
        ConfluenceCondition.CleanPriceAction,
        ConfluenceCondition.CalendarDriver,
        ConfluenceCondition.CalendarClear,
    ];

    private readonly ConfluenceOptions _confluence;
    private readonly SetupCandidateOptions _options;
    private readonly SetupScorer _scorer;
    private readonly IReadOnlySet<ConfluenceCondition> _standingConditions;
    private readonly IReadOnlySet<ConfluenceCondition> _applicable;

    private readonly Dictionary<ConfluenceCondition, Latched> _latched = [];
    private Direction? _lockedDirection;
    private long _mssBarIndex;
    private MarketStructureShift? _anchorMss;

    public SetupCandidate(ConfluenceOptions confluence, SetupCandidateOptions options, SetupScorer scorer)
    {
        ArgumentNullException.ThrowIfNull(confluence);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scorer);
        _confluence = confluence;
        _options = options;
        _scorer = scorer;
        _standingConditions = new HashSet<ConfluenceCondition>(options.StandingConditions);

        // The denominator is the CONSTANT universe of weighted confluences so that matching more optional
        // confluences pushes B → A and setups stay comparable across the run (plan §2.5.4).
        _applicable = new HashSet<ConfluenceCondition>(confluence.Weights.Keys);
    }

    /// <summary>The locked trade direction, or null while no shift has set one.</summary>
    public Direction? LockedDirection => _lockedDirection;

    /// <summary>Whether the candidate is holding any accumulated state (for the session's reset bookkeeping).</summary>
    public bool HasActivity => _latched.Count > 0;

    /// <summary>
    /// Folds one candle's confluence matches into the candidate and returns a <see cref="SetupConfirmation"/>
    /// when the setup confirms at or above the alert floor (resetting itself so it is not re-emitted), else null.
    /// </summary>
    public SetupConfirmation? Observe(MarketContext context, Candle current, IReadOnlyList<ConfluenceMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(matches);

        var barIndex = context.BarsProcessed;

        // (1) Premise teardown: the anchoring MSS was invalidated (ITH/ITL breach, §2.5.7) — the directional
        // premise is gone, so the whole candidate dies before this candle's matches are ingested.
        if (_anchorMss is { IsConfirmed: false })
        {
            Reset();
        }

        // (2) Ingest. Event conditions latch (with TTL); standing conditions are held only for this candle.
        var standing = new Dictionary<ConfluenceCondition, DetectorResult>();
        foreach (var match in matches)
        {
            if (match.Condition == ConfluenceCondition.DisplacementMss)
            {
                // The MSS (re)sets the direction lock — a fresh opposing shift reseeds an intraday reversal.
                _latched[match.Condition] = new Latched(barIndex, match.Result);
                _lockedDirection = match.Result.Direction;
                _mssBarIndex = barIndex;
                _anchorMss = context.LastMss;
            }
            else if (_standingConditions.Contains(match.Condition))
            {
                standing[match.Condition] = match.Result;
            }
            else
            {
                _latched[match.Condition] = new Latched(barIndex, match.Result);
            }
        }

        // (3) Age out latched events past the assembly window; if the locking MSS expired, drop the lock.
        ExpireStale(barIndex);

        if (_lockedDirection is not { } direction)
        {
            return null; // no shift yet → DisplacementMss (required) cannot be matched → nothing to confirm
        }

        // (4) Build the direction-consistent matched set + the per-condition reasoning.
        var contributions = new Dictionary<ConfluenceCondition, ConfluenceContribution>();
        var matched = new HashSet<ConfluenceCondition>();

        foreach (var (condition, latched) in _latched)
        {
            if (!DirectionAllows(latched.Result.Direction, direction))
            {
                continue; // contradicts the lock (e.g. the wrong premium/discount half) → not counted
            }

            // The sweep must strictly precede the MSS (§2.5 steps 4 then 5): a sweep latched AFTER the shift is
            // a new liquidity event seeding a future setup, not completion of this one.
            if (condition == ConfluenceCondition.LiquiditySweep && latched.BarIndex > _mssBarIndex)
            {
                continue;
            }

            matched.Add(condition);
            contributions[condition] = ToContribution(condition, latched.Result);
        }

        foreach (var (condition, result) in standing)
        {
            if (!DirectionAllows(result.Direction, direction))
            {
                continue;
            }

            matched.Add(condition);
            contributions[condition] = ToContribution(condition, result);
        }

        // (5) Grade against the constant weighted universe; a missing RequiredCondition forces Reject.
        var score = _scorer.Score(matched, _applicable);
        if (!score.MeetsAlertFloor(_confluence.AlertMinimumGrade))
        {
            return null;
        }

        // (6) Confirm and reset so the same setup is not re-emitted on the following candle.
        var confirmation = new SetupConfirmation(
            context.Symbol,
            direction,
            current.Timeframe,
            score.Grade,
            score.Score,
            current.OpenTimeUtc,
            OrderContributions(contributions));
        Reset();
        return confirmation;
    }

    /// <summary>Clears all accumulated state and the direction lock (on confirm, NY rollover, or killzone change).</summary>
    public void Reset()
    {
        _latched.Clear();
        _lockedDirection = null;
        _mssBarIndex = 0;
        _anchorMss = null;
    }

    private void ExpireStale(long barIndex)
    {
        var stale = _latched
            .Where(kv => barIndex - kv.Value.BarIndex > _options.MaxAssemblyBars)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var condition in stale)
        {
            _latched.Remove(condition);
        }

        if (!_latched.ContainsKey(ConfluenceCondition.DisplacementMss))
        {
            _lockedDirection = null;
            _anchorMss = null;
        }
    }

    private static bool DirectionAllows(Direction? conditionDirection, Direction lockedDirection)
        => conditionDirection is null || conditionDirection == lockedDirection;

    private static ConfluenceContribution ToContribution(ConfluenceCondition condition, DetectorResult result)
        => new(condition, result.Direction, result.KeyLevel, result.ReasonFragment);

    private static IReadOnlyList<ConfluenceContribution> OrderContributions(
        IReadOnlyDictionary<ConfluenceCondition, ConfluenceContribution> contributions)
        => ReasonOrder
            .Where(contributions.ContainsKey)
            .Select(condition => contributions[condition])
            .ToList();
}
