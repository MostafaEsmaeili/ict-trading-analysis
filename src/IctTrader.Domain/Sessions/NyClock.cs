namespace IctTrader.Domain.Sessions;

/// <summary>
/// The single point where UTC is converted to New-York wall-clock time (plan §4.8). ICT killzones and
/// sessions are defined in NY time with US DST, but the host may run in any zone, so every NY-session
/// calculation goes through this DST-aware service using the IANA id <c>America/New_York</c> (never the
/// Windows id, which ignores IANA rules and is wrong off-Windows). "Now" comes only from the injected
/// BCL <see cref="TimeProvider"/>; conversions are pure, so killzone classification is identical whether
/// the process runs in UTC, Tokyo, or Berlin.
/// </summary>
public sealed class NyClock
{
    public const string NewYorkIanaId = "America/New_York";

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _newYork;

    public NyClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _newYork = ResolveNewYorkZone();
    }

    /// <summary>
    /// Resolves the New-York IANA zone, failing fast with a clear message if ICU is unavailable. A host
    /// startup validator calls this so a mis-provisioned environment never silently mis-times killzones.
    /// </summary>
    public static TimeZoneInfo ResolveNewYorkZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(NewYorkIanaId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"The IANA time zone '{NewYorkIanaId}' could not be resolved. Ensure ICU is available " +
                "(InvariantGlobalization must be false) so NY-session times are correct on any host.",
                ex);
        }
    }

    /// <summary>The current instant in UTC, sourced only from the injected <see cref="TimeProvider"/>.</summary>
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <summary>The current instant expressed in New-York local time.</summary>
    public DateTimeOffset NewYorkNow => ToNewYork(UtcNow);

    /// <summary>Converts any instant to New-York local time (DST-aware).</summary>
    public DateTimeOffset ToNewYork(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, _newYork);

    /// <summary>The New-York wall-clock time-of-day for an instant — the value killzone windows test against.</summary>
    public TimeOnly NewYorkTimeOfDay(DateTimeOffset instant) => TimeOnly.FromTimeSpan(ToNewYork(instant).TimeOfDay);

    /// <summary>The New-York calendar date for an instant — the financial day starts 00:00 NY (plan §2.1).</summary>
    public DateOnly NewYorkDate(DateTimeOffset instant) => DateOnly.FromDateTime(ToNewYork(instant).Date);

    /// <summary>
    /// Whether New York observes daylight saving time at the given instant. Derived from the actual UTC
    /// offset (EDT = base −05:00 shifted to −04:00) rather than <c>IsDaylightSavingTime</c> on a converted
    /// local time, which is wrong on the fall-back ambiguous hour.
    /// </summary>
    public bool IsNewYorkDaylightSaving(DateTimeOffset instant)
        => _newYork.GetUtcOffset(instant) != _newYork.BaseUtcOffset;
}
