using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Host.Calendar;
using Microsoft.Extensions.Options;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Pure tests (no container) for the slice-3 calendar sources: the FMP JSON parser maps + filters the provider feed
/// to the gate-relevant US releases, and the Config source serves operator-supplied events filtered to the window.
/// These live here because the sources are Host-internal (InternalsVisibleTo IctTrader.IntegrationTests).
/// </summary>
public sealed class CalendarSourceTests
{
    private const string FmpFixture = """
    [
      { "event": "FOMC Statement", "date": "2024-01-31 14:00:00", "country": "US", "currency": "USD", "impact": "High" },
      { "event": "Nonfarm Payrolls", "date": "2024-02-02 08:30:00", "country": "US", "currency": "USD", "impact": "High" },
      { "event": "CPI (YoY)", "date": "2024-02-13 08:30:00", "country": "US", "currency": "USD", "impact": "High" },
      { "event": "FOMC Statement", "date": "2024-01-31 14:00:00", "country": "US", "currency": "USD", "impact": "High" },
      { "event": "ECB Rate Decision", "date": "2024-01-25 08:45:00", "country": "EU", "currency": "EUR", "impact": "High" },
      { "event": "Retail Sales", "date": "2024-02-15 08:30:00", "country": "US", "currency": "USD", "impact": "Medium" }
    ]
    """;

    [Fact]
    public void Fmp_parser_maps_us_fomc_nfp_cpi_dedups_and_skips_the_rest()
    {
        var events = FmpCalendarParser.Parse(FmpFixture);

        events.Should().HaveCount(3); // FOMC + NFP + CPI; the duplicate FOMC, the EUR row, and Retail Sales are dropped
        events.Should().ContainSingle(e => e.Type == CalendarEventType.Fomc && e.NyDate == new DateOnly(2024, 1, 31));
        events.Should().ContainSingle(e => e.Type == CalendarEventType.Nfp && e.NyDate == new DateOnly(2024, 2, 2));
        events.Should().ContainSingle(e => e.Type == CalendarEventType.Cpi && e.NyDate == new DateOnly(2024, 2, 13));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")] // not an array
    public void Fmp_parser_returns_empty_for_an_empty_or_non_array_payload(string json)
    {
        FmpCalendarParser.Parse(json).Should().BeEmpty();
    }

    [Fact]
    public async Task Config_source_returns_only_events_inside_the_window()
    {
        var options = Options.Create(new CalendarFeedOptions
        {
            Provider = CalendarProvider.Config,
            Events =
            [
                new CalendarEventConfig { Date = new DateOnly(2024, 1, 10), Type = CalendarEventType.Fomc }, // before window
                new CalendarEventConfig { Date = new DateOnly(2024, 1, 31), Type = CalendarEventType.Fomc }, // in window
                new CalendarEventConfig { Date = new DateOnly(2024, 2, 2), Type = CalendarEventType.Nfp },   // in window
                new CalendarEventConfig { Date = new DateOnly(2024, 3, 20), Type = CalendarEventType.Fomc }, // after window
            ],
        });
        var source = new ConfigEconomicCalendarSource(options);

        var events = await source.FetchAsync(new DateOnly(2024, 1, 28), new DateOnly(2024, 2, 28));

        source.Provider.Should().Be("Config");
        events.Should().HaveCount(2);
        events.Should().OnlyContain(e => e.NyDate >= new DateOnly(2024, 1, 28) && e.NyDate <= new DateOnly(2024, 2, 28));
    }
}
