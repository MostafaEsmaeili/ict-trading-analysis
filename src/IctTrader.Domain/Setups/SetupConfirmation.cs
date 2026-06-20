using IctTrader.Domain.Detection;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Setups;

/// <summary>One matched confluence's contribution to a confirmed setup's reasoning and chart evidence (plan §4.5).</summary>
public readonly record struct ConfluenceContribution(
    ConfluenceCondition Condition,
    Direction? Direction,
    decimal? KeyLevel,
    string ReasonFragment);

/// <summary>
/// The graded, ADVISORY outcome of the confluence FSM (plan §2.5.4/§4.4/§4.5): a direction-locked,
/// veto-cleared setup whose score met the alert floor. It carries the trade direction, grade, score, and the
/// ordered confluence reasoning for an alert and a future priced <c>Setup</c> aggregate — but NEVER an order:
/// <see cref="IsAdvisoryOnly"/> is structurally always true (§6.3 guardrail). The Scanning module maps this to
/// the <c>SetupConfirmed</c> contract event.
/// </summary>
public sealed record SetupConfirmation(
    Symbol Symbol,
    Direction Direction,
    Timeframe Timeframe,
    SetupGrade Grade,
    int Score,
    DateTimeOffset ConfirmedAtUtc,
    IReadOnlyList<ConfluenceContribution> Confluences,
    PricedFrame? Frame)
{
    /// <summary>Structural guardrail: a confirmed setup is analysis only; the system never routes an order (§6.3).</summary>
    public bool IsAdvisoryOnly => true;
}
