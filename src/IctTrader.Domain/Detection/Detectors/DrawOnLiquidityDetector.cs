using IctTrader.Domain.Configuration;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Emits the §2.5.2 RequiredCondition <see cref="ConfluenceCondition.DrawTargetRrMet"/> (weight 0.65, plan
/// §2.5.1 steps 2/8/9): a valid OPPOSING draw-on-liquidity target must exist that gives at least the configured
/// minimum reward-to-risk. The frame is the §2.5 trade — direction from the CONFIRMED MSS (bias-aligned), entry
/// at the shared <see cref="OteEntryResolver"/> array level, stop beyond the swept swing extreme, and the target
/// the nearest UNTAPPED opposite-side liquidity pool beyond the entry (you sweep one side, you draw to the
/// other), excluding the just-swept level and HRLR runs. RR is gated by the active style's MinRewardRatio (never
/// below the hard 2:1 floor). A confluence FEEDER — it prices nothing for execution.
///
/// <para>THIS SLICE draws to registered liquidity pools only; the broader step-2 draw set (prior-day H/L, HTF
/// FVG, big figures) and FVG/OB-anchored or stacked stops are deferred (spec §5).</para>
/// </summary>
public sealed class DrawOnLiquidityDetector : ISetupDetector
{
    private readonly DrawOnLiquidityOptions _options;
    private readonly OteOptions _oteOptions;
    private readonly TradeStyleOptions _styleOptions;
    private readonly FvgOptions _fvgOptions;

    public DrawOnLiquidityDetector(
        DrawOnLiquidityOptions options, OteOptions oteOptions, TradeStyleOptions styleOptions, FvgOptions fvgOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(oteOptions);
        ArgumentNullException.ThrowIfNull(styleOptions);
        ArgumentNullException.ThrowIfNull(fvgOptions);
        _options = options;
        _oteOptions = oteOptions;
        _styleOptions = styleOptions;
        _fvgOptions = fvgOptions;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.DrawTargetRrMet;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Direction comes from the CONFIRMED shift (an invalidated one has no executable direction) and must not
        // be counter to the daily bias (§2.5.1 step 1).
        if (context.LastMss is not { IsConfirmed: true } mss)
        {
            return DetectorResult.NoMatch;
        }

        var direction = mss.Direction;
        if (context.Bias is { } bias && bias != direction)
        {
            return DetectorResult.NoMatch; // never the counter-bias trade
        }

        // The swept swing extreme is the stop reference; it must be the same-direction sweep.
        if (context.LastSweep is not { } sweep || sweep.Direction != direction)
        {
            return DetectorResult.NoMatch;
        }

        // Entry = the shared OTE array level (no current-price fallback — without an array there is no entry).
        // READ-ONLY use of the shared selection (FVG-SEM-2a); the single writer of the entry marker is the
        // OteFibDetector — this detector never marks.
        var policy = new OteEntryResolver.OteSelectionPolicy(_fvgOptions.StrictFirstFvg, _fvgOptions.StackProximityPips);
        if (OteEntryResolver.Resolve(context, _oteOptions, policy) is not { } ote || ote.Direction != direction)
        {
            return DetectorResult.NoMatch;
        }

        var entry = ote.Level;
        var buffer = context.SymbolSpec.PipsToPrice(new Pips(_options.StopBufferPips));

        // Stop beyond the swept extreme; the swept level must sit on the protected side of the entry.
        decimal stop;
        if (direction == Direction.Bullish)
        {
            if (sweep.Level >= entry)
            {
                return DetectorResult.NoMatch; // the swept low must be below the entry
            }

            stop = sweep.Level - buffer;
        }
        else
        {
            if (sweep.Level <= entry)
            {
                return DetectorResult.NoMatch; // the swept high must be above the entry
            }

            stop = sweep.Level + buffer;
        }

        var risk = Math.Abs(entry - stop);
        if (risk <= 0m)
        {
            return DetectorResult.NoMatch;
        }

        var floor = Math.Max(_styleOptions.For(_options.Style).MinRewardRatio, _styleOptions.AbsoluteMinRewardRatio);
        if (NearestQualifyingTarget(context, direction, entry, risk, sweep.Level, floor) is not { } draw)
        {
            return DetectorResult.NoMatch; // no opposing draw clears the floor
        }

        var rewardRatio = new RewardRatio(Math.Abs(draw - entry) / risk);
        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = direction.ToString(),
            [EvidenceKeys.EntryPrice] = entry,
            [EvidenceKeys.StopPrice] = stop,
            [EvidenceKeys.TargetPrice] = draw,
            [EvidenceKeys.RewardRatio] = rewardRatio.Value,
        };

        return DetectorResult.Match(
            direction, draw, ReasonFragments.DrawTarget(direction, draw, rewardRatio.Value), evidence);
    }

    // The nearest untapped opposite-side pool beyond the entry that still clears the RR floor (the conservative
    // valid draw); deterministic tie-break by strength then earliest formation so replay is field-equal.
    private decimal? NearestQualifyingTarget(
        MarketContext context, Direction direction, decimal entry, decimal risk, decimal sweptLevel, decimal floor)
    {
        var targetSide = direction == Direction.Bullish ? LiquiditySide.BuySide : LiquiditySide.SellSide;
        var exclusion = context.SymbolSpec.PipsToPrice(new Pips(_options.SweptLevelExclusionPips));

        LiquidityPool? best = null;
        var bestDistance = decimal.MaxValue;

        foreach (var pool in context.LiquidityPools)
        {
            // Untapped opposite-side liquidity only — a Run/HRLR pool is never Untapped, so it is excluded for
            // free (the §2.5.8 do-not-fade rule).
            if (!pool.Untapped || pool.Side != targetSide)
            {
                continue;
            }

            var level = pool.Level.Value;
            var beyondEntry = direction == Direction.Bullish ? level > entry : level < entry;
            if (!beyondEntry)
            {
                continue;
            }

            if (Math.Abs(level - sweptLevel) <= exclusion)
            {
                continue; // do not draw back to the liquidity just swept
            }

            if (Math.Abs(level - entry) / risk < floor)
            {
                continue; // too close to clear the reward-to-risk floor
            }

            var distance = Math.Abs(level - entry);
            if (best is null || distance < bestDistance || (distance == bestDistance && IsBetterTie(pool, best)))
            {
                best = pool;
                bestDistance = distance;
            }
        }

        return best?.Level.Value;
    }

    private static bool IsBetterTie(LiquidityPool candidate, LiquidityPool current)
        => candidate.Strength > current.Strength
            || (candidate.Strength == current.Strength && candidate.FormedAtUtc < current.FormedAtUtc);
}
