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
        DateTimeOffset confirmedAtUtc,
        decimal? stackedFartherBound = null,
        SetupModel model = SetupModel.Ict2022)
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
        StackedFartherBound = stackedFartherBound;
        Model = model;
    }

    public Symbol Symbol { get; }

    /// <summary>The setup model that confirmed this setup (plan §16) — the discriminator every downstream
    /// consumer (trades, alerts, signals, performance) segments by. Defaults to the canonical §2.5 model so
    /// pre-multi-model construction sites stay byte-identical.</summary>
    public SetupModel Model { get; }

    public TradeStyle Style { get; }

    public Timeframe Timeframe { get; }

    public SetupGrade Grade { get; }

    public int Score { get; }

    public TradePlan Plan { get; }

    public SetupReason Reason { get; }

    public DateTimeOffset ConfirmedAtUtc { get; }

    /// <summary>
    /// FVG-SEM-2b: the far-edge of the deeper stacked FVG the stop already clears (Ep3 L376-413), or null when the
    /// entry was not a stacked first-FVG selection. It is NOT a <see cref="TradePlan"/> tier (it never enters the
    /// stop &lt; entry &lt; T1 &lt; T2 order invariant) — the ARMED entry carries it so the wrong-order NIX can fire
    /// (a retrace that hits the farther gap before the limit fills is no-trade).
    /// </summary>
    public decimal? StackedFartherBound { get; }

    public Direction Direction => Plan.Direction;

    /// <summary>Structural guardrail: a Setup is analysis only; the system never routes an order (§6.3).</summary>
    public bool IsAdvisoryOnly => true;
}
