namespace IctTrader.Domain.Setups;

/// <summary>
/// The confluence grade that gates alerts (plan §2.5.4). A ≥ 80 and B 65–79 both require all
/// RequiredConditions to be true; C 50–64 is a silent watchlist; Reject &lt; 50. Only A and B fire an
/// alert (floor 65). Ordered so higher grade compares greater.
/// </summary>
public enum SetupGrade
{
    Reject = 0,
    C = 1,
    B = 2,
    A = 3,
}
