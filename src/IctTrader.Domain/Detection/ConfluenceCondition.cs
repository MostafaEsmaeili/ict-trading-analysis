namespace IctTrader.Domain.Detection;

/// <summary>
/// The closed set of confluences the scanner scores (plan §2.5.3). Each detector emits exactly one of
/// these; <see cref="SetupGrade"/> is derived from the matched subset. Weights and which are required are
/// NOT baked in here — they live in tunable <c>ConfluenceOptions</c> (plan §4.6, no magic numbers).
/// <see cref="CalendarClear"/> is a hard no-trade gate (§2.5.2), distinct from the low-weight
/// <see cref="CalendarDriver"/> score contributor.
/// </summary>
public enum ConfluenceCondition
{
    KillzoneEntry,
    LiquiditySweep,
    DisplacementMss,
    FvgPresent,
    BiasAligned,
    PremiumDiscountHalf,
    OteZone,
    OrderBlockConfluence,
    DrawTargetRrMet,
    SmtDivergence,
    OpenPriceReference,
    MacroTime,
    CleanPriceAction,
    CalendarDriver,
    CalendarClear,
}
