using IctTrader.Domain.Sessions;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable gate for the OPTIONAL <c>CalendarDriver</c> confluence (decision TGR-3, plan §2.5.2/§2.5.8). DISTINCT from
/// the required hard-gate <c>CalendarClear</c>: a same-NY-day economic DRIVER (a scheduled high-impact release) gives
/// the session a reason to move toward a draw, so it is low-weight scoring confluence — but ONLY on a day NOT inside
/// the hard-gate blackout (which the required <c>CalendarClear</c> owns), so the release minute is never
/// double-counted. A scoring-only confluence (default ON), never a hard gate; fail-OPEN when the calendar is unloaded.
/// Bound from <c>Ict:Detection:CalendarDriver</c>.
/// </summary>
public sealed class CalendarDriverOptions
{
    public const string SectionName = "Ict:Detection:CalendarDriver";

    /// <summary>Whether the calendar-driver confluence is scored. Default ON — additive scoring only.</summary>
    public bool Enabled { get; init; } = true;

    // The driver-type list defaults to EMPTY so the .NET config binder REPLACES rather than APPENDS to a pre-populated
    // initializer (see MarketContextOptions.cs for the documented rationale); the default driver set is applied by the
    // ResolvedDriverEventTypes accessor the detector consumes.
    public IReadOnlyList<CalendarEventType> DriverEventTypes { get; init; } = [];

    // FOMC / NFP / CPI are the canonical high-impact "drivers"; the catch-all Other is excluded by default.
    private static readonly IReadOnlyList<CalendarEventType> DefaultDriverEventTypes =
        [CalendarEventType.Fomc, CalendarEventType.Nfp, CalendarEventType.Cpi];

    /// <summary>The event types that count as session drivers — the configured set, or the canonical high-impact
    /// default when none is configured. Consume this, never the raw <see cref="DriverEventTypes"/>.</summary>
    public IReadOnlyList<CalendarEventType> ResolvedDriverEventTypes =>
        DriverEventTypes.Count == 0 ? DefaultDriverEventTypes : DriverEventTypes;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        // An empty CONFIGURED list is VALID (use the default set, applied by ResolvedDriverEventTypes); a null is not.
        if (DriverEventTypes is null)
        {
            errors.Add("DriverEventTypes must not be null.");
        }

        return errors;
    }
}
