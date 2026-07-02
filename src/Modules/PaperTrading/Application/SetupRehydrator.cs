using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Contracts;

namespace IctTrader.PaperTrading.Application;

/// <summary>
/// Reconstructs a domain <see cref="Setup"/> from the wire <see cref="SetupDto"/> the bus delivers — the
/// consumer half of the scan→trade seam (Architecture A). Scanning produced the DTO by projecting a confirmed,
/// priced advisory Setup; this rebuilds an equivalent one so the PaperTrading factory/orchestrator can size and
/// open a trade. The geometry round-trips FAITHFULLY: the <see cref="TargetLadder"/>/<see cref="TradePlan"/>
/// ctors recompute the reward-to-risk from entry/stop/targets — exactly the numbers the DTO carries — so the
/// rebuilt RR equals the scanned RR and the frozen 1R (=|entry−stop|) is byte-identical (no drift).
///
/// <para><b>Two wire losses, both safe for the default path.</b> (1) The exact within-grade SCORE is not on the
/// wire (the DTO carries the alertable <c>Grade</c>); the rebuilt score is the grade's configured FLOOR
/// (<see cref="ConfluenceOptions.GradeAThreshold"/>/<see cref="ConfluenceOptions.GradeBThreshold"/>) — grade-
/// consistent and unused by the trade path (<c>PaperTradeFactory</c> reads only Plan/Symbol/Style/Timeframe).
/// (2) <see cref="Setup.StackedFartherBound"/> (the FVG-SEM-2b wrong-order NIX) is not a <see cref="SetupDto"/>
/// field, so it rebuilds as null — only material under the non-default <c>StrictFirstFvg</c>; carry it on the
/// contract before enabling that on the live trade path.</para>
///
/// <para><b>Wire convention coupling:</b> the canonical target order (T1 at index 0, runner next, deeper SD
/// tiers after) mirrors how Scanning projects <c>TradePlan.TargetLadder.Targets</c>, so the runner index is
/// reconstructed as <see cref="TargetLadder.CanonicalRunnerIndex"/> — the SINGLE shared const the Scanning
/// factory also pins, so producer and consumer cannot drift the runner tier across the wire.</para>
/// </summary>
internal static class SetupRehydrator
{
    public static Setup ToDomain(SetupDto dto, ConfluenceOptions grading)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(grading);

        var direction = ParseEnum<Direction>(dto.Direction);
        var grade = ParseEnum<SetupGrade>(dto.Grade);

        var ladder = new TargetLadder(
            direction,
            dto.Targets.Select(price => new Price(price)).ToList(),
            TargetLadder.CanonicalRunnerIndex);
        var plan = new TradePlan(direction, new Price(dto.Entry), new Price(dto.Stop), ladder);

        return new Setup(
            new Symbol(dto.Symbol),
            ParseEnum<TradeStyle>(dto.Style),
            ParseEnum<Timeframe>(dto.TriggerTimeframe),
            grade,
            GradeFloor(grade, grading),
            plan,
            new SetupReason(dto.Reason),
            dto.DetectedAtUtc.ToUniversalTime(),
            stackedFartherBound: null,
            model: ParseEnum<SetupModel>(dto.Model));
    }

    private static int GradeFloor(SetupGrade grade, ConfluenceOptions grading) => grade switch
    {
        SetupGrade.A => grading.GradeAThreshold,
        SetupGrade.B => grading.GradeBThreshold,
        _ => throw new ArgumentOutOfRangeException(
            nameof(grade), grade, "Only an A or B setup is delivered on the wire (the §2.5.4 alert floor)."),
    };

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        // IsDefined rejects numeric strings (TryParse accepts "99" as the undefined (T)99) — the wire must carry
        // a defined member NAME, so an out-of-range value fails fast rather than building a garbage setup.
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new FormatException($"'{value}' is not a valid {typeof(T).Name} member.");
}
