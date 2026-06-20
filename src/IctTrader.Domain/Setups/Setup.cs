using IctTrader.Domain.Common;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Setups;

/// <summary>
/// The confirmed, ADVISORY, priced setup (plan §3.0/§4.5) — the aggregate root Alerting and PaperTrading
/// consume. It exists ONLY for an alertable grade (A or B, §2.5.4); a C/watchlist or a Reject never becomes a
/// Setup. It carries the priced <see cref="TradePlan"/>, the composed <see cref="SetupReason"/>, the trade
/// style + trigger timeframe (which name the max-hold / no-overnight policy the simulator applies), the grade,
/// and the detection time. <see cref="IsAdvisoryOnly"/> is structurally true — there is no order field and no
/// executor reference (§6.3 guardrail).
/// </summary>
public sealed class Setup
{
    public Setup(
        Symbol symbol,
        TradeStyle style,
        Timeframe timeframe,
        SetupGrade grade,
        int score,
        TradePlan plan,
        SetupReason reason,
        DateTimeOffset confirmedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        Guard.Against(
            grade is not (SetupGrade.A or SetupGrade.B),
            "Only an A or B grade becomes an advisory Setup (the §2.5.4 alert floor).");
        Guard.Against(score is < 0 or > 100, $"Setup score must be within [0, 100] but was {score}.");
        Guard.Against(confirmedAtUtc.Offset != TimeSpan.Zero, "Setup.ConfirmedAtUtc must be UTC.");

        Symbol = symbol;
        Style = style;
        Timeframe = timeframe;
        Grade = grade;
        Score = score;
        Plan = plan;
        Reason = reason;
        ConfirmedAtUtc = confirmedAtUtc;
    }

    public Symbol Symbol { get; }

    public TradeStyle Style { get; }

    public Timeframe Timeframe { get; }

    public SetupGrade Grade { get; }

    public int Score { get; }

    public TradePlan Plan { get; }

    public SetupReason Reason { get; }

    public DateTimeOffset ConfirmedAtUtc { get; }

    public Direction Direction => Plan.Direction;

    /// <summary>Structural guardrail: a Setup is analysis only; the system never routes an order (§6.3).</summary>
    public bool IsAdvisoryOnly => true;
}
