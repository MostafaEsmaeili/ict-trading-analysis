using FluentAssertions;
using IctTrader.Domain.Services;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Services;

/// <summary>
/// Locks the pure <see cref="CandleAggregator"/> resampler (plan §15): buckets are aligned to the target period, each
/// bucket folds first-open / max-high / min-low / last-close / summed-volume, and resampling only ever goes UP
/// (a coarser target than the source). This is the backtest's fallback for a timeframe not fetched natively.
/// </summary>
public sealed class CandleAggregatorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static Candle M1(int minute, decimal open, decimal high, decimal low, decimal close, decimal volume = 1m)
        => new(Eurusd, Timeframe.M1, Base.AddMinutes(minute), open, high, low, close, volume);

    [Fact]
    public void Resamples_five_M1_candles_into_one_M5_with_the_ohlcv_fold()
    {
        var source = new[]
        {
            M1(0, 1.0800m, 1.0810m, 1.0795m, 1.0805m, 10m),
            M1(1, 1.0805m, 1.0820m, 1.0800m, 1.0815m, 12m),
            M1(2, 1.0815m, 1.0818m, 1.0788m, 1.0795m, 8m),
            M1(3, 1.0795m, 1.0808m, 1.0789m, 1.0806m, 9m),
            M1(4, 1.0806m, 1.0812m, 1.0802m, 1.0809m, 11m),
        };

        var result = CandleAggregator.Resample(source, Timeframe.M5);

        result.Should().ContainSingle();
        var bar = result[0];
        bar.Timeframe.Should().Be(Timeframe.M5);
        bar.OpenTimeUtc.Should().Be(Base);          // aligned to the 07:00 bucket start
        bar.Open.Should().Be(1.0800m);              // first member's open
        bar.High.Should().Be(1.0820m);              // max high
        bar.Low.Should().Be(1.0788m);               // min low
        bar.Close.Should().Be(1.0809m);             // last member's close
        bar.Volume.Should().Be(50m);                // summed
    }

    [Fact]
    public void Splits_members_into_buckets_aligned_to_the_target_period()
    {
        // 6 contiguous M1 bars (07:00..07:05) → two M5 buckets: 07:00 (5 bars) and 07:05 (1 bar).
        var source = Enumerable.Range(0, 6).Select(i => M1(i, 1.0800m, 1.0805m, 1.0795m, 1.0800m)).ToList();

        var result = CandleAggregator.Resample(source, Timeframe.M5);

        result.Should().HaveCount(2);
        result[0].OpenTimeUtc.Should().Be(Base);
        result[1].OpenTimeUtc.Should().Be(Base.AddMinutes(5));
    }

    [Fact]
    public void Same_timeframe_returns_the_source_unchanged()
    {
        IReadOnlyList<Candle> source = [M1(0, 1.0800m, 1.0805m, 1.0795m, 1.0800m)];

        CandleAggregator.Resample(source, Timeframe.M1).Should().BeSameAs(source);
    }

    [Fact]
    public void Throws_when_the_target_is_finer_than_the_source()
    {
        IReadOnlyList<Candle> source = [new Candle(Eurusd, Timeframe.M5, Base, 1.0800m, 1.0805m, 1.0795m, 1.0800m, 1m)];

        var act = () => CandleAggregator.Resample(source, Timeframe.M1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Empty_source_yields_empty()
        => CandleAggregator.Resample([], Timeframe.M5).Should().BeEmpty();
}
