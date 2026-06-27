using IctTrader.Domain.Sessions;

namespace IctTrader.Domain.Configuration;

/// <summary>The economic-calendar data source (plan §2.5.8, §15). <see cref="Config"/> reads an operator-supplied
/// list of dates (the offline default — FOMC/NFP schedules are published a year ahead); <see cref="Fmp"/> fetches
/// from the Financial Modeling Prep economic-calendar API (read-only HTTP).</summary>
public enum CalendarProvider
{
    Config,
    Fmp,
}

/// <summary>One operator-supplied economic event for the <see cref="CalendarProvider.Config"/> source — a NY-date
/// plus its type (bound from <c>Ict:Calendar:Events</c>). The binder parses the ISO date + the enum member name.</summary>
public sealed record CalendarEventConfig
{
    public DateOnly Date { get; init; }

    public CalendarEventType Type { get; init; } = CalendarEventType.Other;
}

/// <summary>The Financial Modeling Prep economic-calendar provider config (<c>Ict:Calendar:Fmp</c>). The API key is
/// supplied via env/secret, NEVER committed.</summary>
public sealed class FmpCalendarOptions
{
    /// <summary>The FMP REST base URL (the stable-v3 economic-calendar host).</summary>
    public string BaseUrl { get; init; } = "https://financialmodelingprep.com";

    /// <summary>The FMP API key (required when the Fmp provider is selected; supplied via env, never committed).</summary>
    public string? ApiKey { get; init; }
}

/// <summary>
/// The economic-calendar feed configuration (<c>Ict:Calendar</c>, plan §2.5.8/§15). Selects the source, the refresh
/// cadence, and the NY-date window to fetch around "today", so the host's loader can keep the
/// <see cref="IEconomicCalendarStore"/> current and the §2.5.2 gate fires on real FOMC/NFP days. Validated at startup.
/// </summary>
public sealed class CalendarFeedOptions
{
    public const string SectionName = "Ict:Calendar";

    /// <summary>Whether the calendar loader runs at all. OFF by default so a bare host stays a pure API surface (the
    /// gate then fails per its unverified posture — fail-open by default) until an operator opts a source in.</summary>
    public bool Enabled { get; init; }

    /// <summary>Which source to load from (default <see cref="CalendarProvider.Config"/> — offline, operator-supplied).</summary>
    public CalendarProvider Provider { get; init; } = CalendarProvider.Config;

    /// <summary>How often the loader refreshes the store (hours). The Config source is static, but a refresh keeps an
    /// HTTP provider current and re-anchors the fetch window as the NY day advances.</summary>
    public int RefreshHours { get; init; } = 12;

    /// <summary>NY days behind "today" to include (covers a just-passed FOMC whose post-day blackout still applies).</summary>
    public int LookbackDays { get; init; } = 7;

    /// <summary>NY days ahead of "today" to include (covers upcoming NFP-week blackouts the scanner will hit).</summary>
    public int LookaheadDays { get; init; } = 45;

    /// <summary>The operator-supplied events for the <see cref="CalendarProvider.Config"/> source (empty otherwise).</summary>
    public IReadOnlyList<CalendarEventConfig> Events { get; init; } = [];

    /// <summary>The Financial Modeling Prep provider config (used only when <see cref="Provider"/> is Fmp).</summary>
    public FmpCalendarOptions Fmp { get; init; } = new();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(Provider))
        {
            errors.Add($"Provider must be a valid {nameof(CalendarProvider)} but was {(int)Provider}.");
        }

        if (RefreshHours < 1)
        {
            errors.Add($"RefreshHours must be at least 1 but was {RefreshHours}.");
        }

        if (LookbackDays < 0)
        {
            errors.Add($"LookbackDays must be >= 0 but was {LookbackDays}.");
        }

        if (LookaheadDays < 1)
        {
            errors.Add($"LookaheadDays must be at least 1 but was {LookaheadDays}.");
        }

        // A configured event must carry a defined type; the date binds to default(DateOnly) only if omitted, which
        // would be a 0001-01-01 event the gate ignores — flag it so a typo'd entry doesn't silently do nothing.
        for (var i = 0; i < Events.Count; i++)
        {
            if (!Enum.IsDefined(Events[i].Type))
            {
                errors.Add($"Events[{i}].Type must be a valid {nameof(CalendarEventType)}.");
            }

            if (Events[i].Date == default)
            {
                errors.Add($"Events[{i}].Date is missing or unparseable (expected an ISO yyyy-MM-dd NY date).");
            }
        }

        // Only require the FMP key when FMP is actually the selected, enabled source.
        if (Enabled && Provider == CalendarProvider.Fmp && string.IsNullOrWhiteSpace(Fmp.ApiKey))
        {
            errors.Add($"{SectionName}:Fmp:ApiKey is required when the Fmp calendar provider is enabled.");
        }

        return errors;
    }
}
