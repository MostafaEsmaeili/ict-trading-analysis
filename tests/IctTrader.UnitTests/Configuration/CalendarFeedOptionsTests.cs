using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;

namespace IctTrader.UnitTests.Configuration;

/// <summary>Locks the <c>Ict:Calendar</c> feed-options validation (slice 3): sane defaults pass; bad cadence/window,
/// a malformed event, and an enabled-FMP-without-key all fail with a reason.</summary>
public sealed class CalendarFeedOptionsTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        new CalendarFeedOptions().Validate().Should().BeEmpty();
    }

    [Fact]
    public void A_valid_config_source_with_events_passes()
    {
        var options = new CalendarFeedOptions
        {
            Enabled = true,
            Provider = CalendarProvider.Config,
            Events =
            [
                new CalendarEventConfig { Date = new DateOnly(2024, 1, 31), Type = CalendarEventType.Fomc },
                new CalendarEventConfig { Date = new DateOnly(2024, 2, 2), Type = CalendarEventType.Nfp },
            ],
        };

        options.Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_refresh_interval_fails(int hours)
    {
        new CalendarFeedOptions { RefreshHours = hours }.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void An_event_with_a_missing_date_fails()
    {
        var options = new CalendarFeedOptions
        {
            Events = [new CalendarEventConfig { Type = CalendarEventType.Fomc }], // Date omitted → default
        };

        options.Validate().Should().Contain(e => e.Contains("Date"));
    }

    [Fact]
    public void Enabled_fmp_without_an_api_key_fails()
    {
        var options = new CalendarFeedOptions { Enabled = true, Provider = CalendarProvider.Fmp };

        options.Validate().Should().Contain(e => e.Contains("ApiKey"));
    }

    [Fact]
    public void Fmp_selected_but_disabled_does_not_require_a_key()
    {
        new CalendarFeedOptions { Enabled = false, Provider = CalendarProvider.Fmp }.Validate().Should().BeEmpty();
    }
}
