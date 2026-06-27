using System.Globalization;
using System.Text.Json;
using IctTrader.Domain.Sessions;

namespace IctTrader.Host.Calendar;

/// <summary>
/// Pure parser for the Financial Modeling Prep economic-calendar JSON (an array of
/// <c>{ event, date, country, currency, impact }</c>). It keeps ONLY the US (USD) events the §2.5.2 gate cares about
/// — FOMC rate decisions, Non-Farm Payrolls, and CPI — mapping each by name to a <see cref="CalendarEventType"/> and
/// NY-date-keying it from the date portion (the announcement's NY calendar day). Separated from the HTTP client so it
/// is unit-tested against a captured-shape fixture with no network. Unknown/irrelevant rows are skipped, not failed.
/// </summary>
internal static class FmpCalendarParser
{
    public static IReadOnlyList<EconomicEvent> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var events = new List<EconomicEvent>();
        var seen = new HashSet<(DateOnly, CalendarEventType)>();

        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (!IsUnitedStates(row))
            {
                continue;
            }

            var name = GetString(row, "event");
            if (Classify(name) is not { } type)
            {
                continue; // not one of the gate-relevant releases
            }

            if (!TryReadNyDate(row, out var date))
            {
                continue;
            }

            // De-dup: a provider can list the same release twice (e.g. forecast + actual rows).
            if (seen.Add((date, type)))
            {
                events.Add(new EconomicEvent(date, type));
            }
        }

        return events;
    }

    private static bool IsUnitedStates(JsonElement row)
    {
        var country = GetString(row, "country");
        var currency = GetString(row, "currency");
        return string.Equals(country, "US", StringComparison.OrdinalIgnoreCase)
            || string.Equals(country, "USA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Maps an FMP event name to the gate type, or null when the release is not gate-relevant.</summary>
    private static CalendarEventType? Classify(string name)
    {
        if (name.Length == 0)
        {
            return null;
        }

        if (name.Contains("FOMC", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Federal Funds", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Fed Interest Rate", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Interest Rate Decision", StringComparison.OrdinalIgnoreCase))
        {
            return CalendarEventType.Fomc;
        }

        if (name.Contains("Nonfarm", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Non-Farm", StringComparison.OrdinalIgnoreCase))
        {
            return CalendarEventType.Nfp;
        }

        if (name.Contains("CPI", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Consumer Price Index", StringComparison.OrdinalIgnoreCase))
        {
            return CalendarEventType.Cpi;
        }

        return null;
    }

    /// <summary>Reads the NY calendar date from the FMP "date" field (e.g. "2026-01-28 14:00:00" → 2026-01-28).</summary>
    private static bool TryReadNyDate(JsonElement row, out DateOnly date)
    {
        date = default;
        var raw = GetString(row, "date");
        if (raw.Length == 0)
        {
            return false;
        }

        var datePart = raw.Length >= 10 ? raw[..10] : raw; // the leading yyyy-MM-dd
        return DateOnly.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static string GetString(JsonElement row, string property) =>
        row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
