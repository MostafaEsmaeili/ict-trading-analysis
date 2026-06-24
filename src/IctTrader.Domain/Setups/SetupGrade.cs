namespace IctTrader.Domain.Setups;

/// <summary>
/// The confluence grade that gates alerts (plan §2.5.4 / core-model decisions register TGR-4). An
/// all-RequiredConditions-clean setup grades B, promoted to A at the configured A threshold; any missing
/// RequiredCondition ⇒ Reject. Only A and B fire an alert. C (the §2.5.4 watchlist tier) is retained for the
/// future alert-seam work but is no longer produced by grading. Ordered so a higher grade compares greater.
/// </summary>
public enum SetupGrade
{
    Reject = 0,
    C = 1,
    B = 2,
    A = 3,
}
