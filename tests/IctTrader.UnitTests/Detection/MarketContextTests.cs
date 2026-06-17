using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the per-symbol state container (plan §4.1): newest-at-[^1] ring buffers with capacity eviction,
/// the session recomputed on every append via the killzone clock, dead-array pruning, and the symbol guard.
/// </summary>
public class MarketContextTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly KillzoneClock Clock = new(new NyClock(TimeProvider.System), KillzoneSchedule.CreateDefault());

    private static MarketContext NewContext(int windowCapacity = 512) =>
        new(SymbolSpec.FxMajor(Eurusd), Clock, new MarketContextOptions { WindowCapacity = windowCapacity });

    private static Candle Candle(DateTimeOffset openUtc, decimal close = 1.0850m, Timeframe tf = Timeframe.M5)
        => new(Eurusd, tf, openUtc, 1.0840m, 1.0900m, 1.0800m, close, 1m);

    [Fact]
    public void Window_keeps_newest_at_the_end_and_evicts_past_capacity()
    {
        var ctx = NewContext(windowCapacity: 3);
        var start = new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 4; i++)
        {
            ctx.Append(Candle(start.AddMinutes(5 * i), close: 1.0800m + (0.0001m * i)));
        }

        var window = ctx.Window(Timeframe.M5);
        window.Count.Should().Be(3);
        window[^1].Close.Should().Be(1.0803m); // the 4th (newest) candle
        window[0].Close.Should().Be(1.0801m);  // oldest survivor (the 1st was evicted)
    }

    [Fact]
    public void Session_is_recomputed_on_each_append()
    {
        var ctx = NewContext();

        ctx.Append(Candle(new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero)));   // NY 03:00 -> LondonOpen
        ctx.Session.Killzone.Should().Be(Killzone.LondonOpen);

        ctx.Append(Candle(new DateTimeOffset(2024, 7, 1, 16, 30, 0, TimeSpan.Zero))); // NY 12:30 -> lunch
        ctx.Session.Killzone.Should().Be(Killzone.None);
        ctx.Session.LunchBlocked.Should().BeTrue();
    }

    [Fact]
    public void Mitigated_arrays_are_pruned_on_the_next_append()
    {
        var ctx = NewContext();
        var fvg = new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0832m), new Price(1.0840m),
            new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero));
        ctx.RegisterFvg(fvg);
        ctx.OpenFvgs.Should().HaveCount(1);

        fvg.Mitigate();
        ctx.Append(Candle(new DateTimeOffset(2024, 7, 1, 7, 5, 0, TimeSpan.Zero)));

        ctx.OpenFvgs.Should().BeEmpty();
    }

    [Fact]
    public void Append_rejects_a_candle_for_a_different_symbol()
    {
        var ctx = NewContext();
        var foreign = new Candle(new Symbol("GBPUSD"), Timeframe.M5,
            new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero), 1.25m, 1.26m, 1.24m, 1.255m, 1m);

        var act = () => ctx.Append(foreign);

        act.Should().Throw<DomainException>().WithMessage("*symbol*");
    }

    [Fact]
    public void Replaying_the_same_candles_yields_the_same_state()
    {
        var start = new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 10).Select(i => Candle(start.AddMinutes(5 * i))).ToList();

        var a = NewContext();
        var b = NewContext();
        foreach (var candle in candles)
        {
            a.Append(candle);
            b.Append(candle);
        }

        a.Session.Should().Be(b.Session);
        a.Window(Timeframe.M5).Should().BeEquivalentTo(b.Window(Timeframe.M5));
    }
}
