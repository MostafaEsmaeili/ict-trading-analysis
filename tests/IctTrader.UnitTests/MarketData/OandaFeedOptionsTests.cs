using FluentAssertions;
using IctTrader.MarketData.Infrastructure.Feeds;

namespace IctTrader.UnitTests.MarketData;

/// <summary>
/// Validates the multi-granularity knobs on <see cref="OandaFeedOptions"/> (the live/backtest feed reads every
/// configured instrument × granularity): <see cref="OandaFeedOptions.Granularities"/> /
/// <see cref="OandaFeedOptions.ResolvedGranularities"/> (empty ⇒ fall back to the single
/// <see cref="OandaFeedOptions.Granularity"/>, so an existing single-granularity config stays byte-identical) and
/// <see cref="OandaFeedOptions.MaxConcurrentFetchesPerPoll"/>, plus the fail-fast <see cref="OandaFeedOptions.Validate"/>.
/// </summary>
public class OandaFeedOptionsTests
{
    private static OandaFeedOptions Valid(
        string? granularity = null,
        IReadOnlyList<string>? granularities = null,
        int? maxConcurrentFetchesPerPoll = null) => new()
        {
            BaseUrl = "https://api-fxpractice.oanda.com",
            Token = "test-practice-token",
            Instruments = ["EUR_USD"],
            Granularity = granularity ?? "M5",
            Granularities = granularities ?? [],
            MaxConcurrentFetchesPerPoll = maxConcurrentFetchesPerPoll ?? 6,
        };

    [Fact]
    public void Default_options_validate_clean()
    {
        Valid().Validate().Should().BeEmpty("a token + base URL is all a feed needs; the rest have sane defaults");
    }

    [Fact]
    public void ResolvedGranularities_falls_back_to_the_single_Granularity_when_empty()
    {
        var options = Valid(granularity: "M15", granularities: []);

        // An empty list means "stream just the single Granularity" (the byte-identical single-granularity fallback).
        options.ResolvedGranularities.Should().ContainSingle().Which.Should().Be("M15");
    }

    [Fact]
    public void ResolvedGranularities_uses_the_configured_list_de_duplicated_when_non_empty()
    {
        var options = Valid(granularities: ["M1", "M5", "M5", "M15", "H1"]);

        options.ResolvedGranularities.Should().Equal("M1", "M5", "M15", "H1");
    }

    [Fact]
    public void Validate_rejects_an_unsupported_granularity_in_the_list()
    {
        // OANDA's M2 has no scanner Timeframe member, so a typo must fail fast at startup, not at the first fetch.
        var options = Valid(granularities: ["M5", "M2"]);

        options.Validate().Should().Contain(e => e.Contains("M2", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_accepts_a_supported_multi_granularity_list()
    {
        var options = Valid(granularities: ["M1", "M5", "M15", "H1"]);

        options.Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25)]
    public void Validate_rejects_an_out_of_range_MaxConcurrentFetchesPerPoll(int value)
    {
        var options = Valid(maxConcurrentFetchesPerPoll: value);

        options.Validate().Should().Contain(e => e.Contains("MaxConcurrentFetchesPerPoll", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_accepts_a_positive_MaxConcurrentFetchesPerPoll()
    {
        Valid(maxConcurrentFetchesPerPoll: 1).Validate().Should().BeEmpty();
    }
}
