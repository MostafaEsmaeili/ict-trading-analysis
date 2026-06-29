using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable ranking + feed policy for the cross-matrix "best opportunities" signals feed (bound from
/// <c>Ict:Signals</c>) — "the system suggests the best setup". EVERYTHING is operator-tunable, no magic numbers: how
/// big the feed is (<see cref="MaxFeedSize"/>), how long a confirmed setup stays "live" in the feed
/// (<see cref="RecencyCutoffMinutes"/>), the per-timeframe tie-break priority (<see cref="TimeframePriority"/>), and
/// the grade floor below which a setup never enters the feed (<see cref="MinGrade"/>). The pure
/// <see cref="IctTrader.Domain.Confluence.SignalRanker"/> consumes the ranking weights; the
/// <c>SignalFeedStore</c>/<c>SignalRankingService</c> consume the size/recency/floor. The Host validates this via
/// <c>ValidateOnStart</c> by calling <see cref="Validate"/>.
/// <para><b>Advisory-only (plan §6.3):</b> the feed is a read-only projection of confirmed advisory setups; ranking
/// them routes nowhere near an order path.</para>
/// </summary>
public sealed class SignalRankingOptions
{
    public const string SectionName = "Ict:Signals";

    /// <summary>An upper bound on the feed cap so a misconfigured value can't request an unbounded feed.</summary>
    private const int AbsoluteMaxFeedSize = 500;

    /// <summary>The maximum number of ranked signals retained + returned by the feed (the top-N). Default 25 — a
    /// generous "best opportunities" board across the whole matrix without becoming an audit log.</summary>
    public int MaxFeedSize { get; init; } = 25;

    /// <summary>How long (minutes) after its detection a confirmed setup stays "live" in the feed before it ages out as
    /// stale — a §2.5 intraday opportunity is fleeting, so an old setup should not keep topping the board. Default 240
    /// (4 hours), comfortably spanning a killzone-to-killzone window. INVENTED/operator-tunable — not transcript-verbatim.</summary>
    public int RecencyCutoffMinutes { get; init; } = 240;

    /// <summary>
    /// Per-timeframe ranking priority — HIGHER ranks first when grade, score and reward-to-risk all tie. It encodes
    /// the §4.7 top-down preference that, all else equal, a higher-timeframe confirmation is the higher-conviction
    /// opportunity (less noise). A dictionary (merged by key by the binder, so a non-empty default is safe — unlike a
    /// list, see <see cref="ConfluenceOptions.RequiredConditions"/>); a timeframe absent from the map ranks at
    /// <see cref="DefaultTimeframePriority"/> (the lowest), so it never throws on an unmapped TF.
    /// </summary>
    public IReadOnlyDictionary<Timeframe, int> TimeframePriority { get; init; } = DefaultPriorities;

    /// <summary>The grade floor a setup must meet to ENTER the feed (A = only A; B = A+B). Default B = the §2.5.4 alert
    /// floor, so the feed surfaces exactly the alertable setups. A query may raise the floor further (never lower it).</summary>
    public SetupGrade MinGrade { get; init; } = SetupGrade.B;

    /// <summary>The priority of a timeframe absent from <see cref="TimeframePriority"/> — the lowest, so an unmapped TF
    /// sorts last on the tie-break (never an exception).</summary>
    public const int DefaultTimeframePriority = 0;

    /// <summary>
    /// The default §4.7 priorities: higher timeframe ⇒ higher priority. M1 (the noisiest) lowest; the swing/position
    /// frames highest. Operator-tunable from <c>Ict:Signals:TimeframePriority</c> (merged by key).
    /// </summary>
    private static readonly IReadOnlyDictionary<Timeframe, int> DefaultPriorities = new Dictionary<Timeframe, int>
    {
        [Timeframe.M1] = 1,
        [Timeframe.M3] = 2,
        [Timeframe.M5] = 3,
        [Timeframe.M15] = 4,
        [Timeframe.M30] = 5,
        [Timeframe.H1] = 6,
        [Timeframe.H4] = 7,
        [Timeframe.D1] = 8,
        [Timeframe.W1] = 9,
        [Timeframe.MN1] = 10,
    };

    /// <summary>The priority for <paramref name="timeframe"/> — its configured value, or
    /// <see cref="DefaultTimeframePriority"/> when unmapped (never throws).</summary>
    public int PriorityFor(Timeframe timeframe) =>
        TimeframePriority.TryGetValue(timeframe, out var priority) ? priority : DefaultTimeframePriority;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MaxFeedSize is < 1 or > AbsoluteMaxFeedSize)
        {
            errors.Add($"MaxFeedSize must be within [1, {AbsoluteMaxFeedSize}] but was {MaxFeedSize}.");
        }

        if (RecencyCutoffMinutes < 1)
        {
            errors.Add($"RecencyCutoffMinutes must be at least 1 but was {RecencyCutoffMinutes}.");
        }

        if (!Enum.IsDefined(MinGrade))
        {
            errors.Add($"MinGrade must be a defined SetupGrade but was {(int)MinGrade}.");
        }
        else if (MinGrade is not (SetupGrade.A or SetupGrade.B))
        {
            // Only A and B are alertable (§2.5.4); a C/Reject floor would admit non-tradeable setups to the feed.
            errors.Add($"MinGrade must be A or B (the alertable §2.5.4 grades) but was {MinGrade}.");
        }

        if (TimeframePriority is null)
        {
            errors.Add("TimeframePriority must not be null.");
        }

        return errors;
    }
}
