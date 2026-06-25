using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the per-symbol state container (plan §4.1): newest-at-[^1] ring buffers with capacity eviction,
/// the session recomputed on every append via the killzone clock, dead-array pruning, and the symbol guard.
/// </summary>
public class MarketContextTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero));
    private static readonly KillzoneClock Clock = new(new NyClock(Time), KillzoneSchedule.CreateDefault());

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

    [Fact]
    public void A_new_york_day_rollover_resets_session_scoped_state()
    {
        var ctx = NewContext();
        ctx.Append(Candle(new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero))); // NY day 1
        ctx.SetBias(Direction.Bullish);
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0850m,
            new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero), ctx.BarsProcessed));

        // The next financial day (08:00 NY on 2024-07-02) crosses 00:00 NY -> intraday state clears.
        ctx.Append(Candle(new DateTimeOffset(2024, 7, 2, 12, 0, 0, TimeSpan.Zero)));

        ctx.Bias.Should().BeNull();
        ctx.LastSweep.Should().BeNull();
        ctx.MidnightOpen.Should().Be(1.0840m); // re-anchored to the new day's first candle open
    }

    [Fact]
    public void The_first_candle_initialises_the_day_without_clearing_seeded_state()
    {
        var ctx = NewContext();
        ctx.SetBias(Direction.Bullish); // a warm-started bias seeded before any candle

        ctx.Append(Candle(new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero)));

        ctx.Bias.Should().Be(Direction.Bullish); // initialisation is NOT a rollover, so it must not reset
        ctx.MidnightOpen.Should().Be(1.0840m);
    }

    // --- TIME-10: 08:30 NY macro reference-open capture (Ep17 L154-159) ------------------------

    // 2024-07-01 is EDT (UTC-4): NY 00:00 = UTC 04:00, NY 08:30 = UTC 12:30.
    private static readonly DateTimeOffset NyMidnightUtc = new(2024, 7, 1, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NyMacro0830Utc = new(2024, 7, 1, 12, 30, 0, TimeSpan.Zero);

    // A candle whose Open is explicit (the 08:30 macro reference is the bar's OPEN), respecting the OHLC invariants.
    private static Candle OpenAt(DateTimeOffset openUtc, decimal open)
        => new(Eurusd, Timeframe.M5, openUtc, open, open + 0.0010m, open - 0.0010m, open, 1m);

    [Fact]
    public void Macro_open_is_captured_inclusively_at_eight_thirty_ny()
    {
        var ctx = NewContext();
        ctx.Append(OpenAt(NyMidnightUtc, 1.0800m));               // NY 00:00 -> seeds the midnight open, macro still null
        ctx.MacroOpen.Should().BeNull();

        ctx.Append(OpenAt(NyMacro0830Utc, 1.0875m));              // NY 08:30 exactly -> inclusive capture
        ctx.MacroOpen.Should().Be(1.0875m);
        ctx.MidnightOpen.Should().Be(1.0800m);                   // midnight reference is untouched
    }

    [Fact]
    public void Macro_open_stays_null_before_eight_thirty_ny()
    {
        var ctx = NewContext();
        ctx.Append(OpenAt(NyMidnightUtc, 1.0800m));                              // NY 00:00
        ctx.Append(OpenAt(NyMacro0830Utc.AddMinutes(-5), 1.0860m));             // NY 08:25 -> before macro

        ctx.MacroOpen.Should().BeNull();
        ctx.MidnightOpen.Should().Be(1.0800m);
    }

    [Fact]
    public void Macro_open_captures_the_first_bar_opening_at_or_after_eight_thirty_not_one_straddling_it()
    {
        // Deferred semantics (register TIME-10): on a non-aligned feed the macro open is the OPEN of the first bar
        // opening AT/AFTER 08:30 NY (only OHLC is available, not the intrabar 08:30 print). A bar opening 08:28 that
        // trades through 08:30 does NOT capture; a later bar that gaps over 08:30 captures its own open.
        var ctx = NewContext();
        ctx.Append(OpenAt(NyMidnightUtc, 1.0800m));                             // NY 00:00
        ctx.Append(OpenAt(NyMacro0830Utc.AddMinutes(-2), 1.0860m));             // opens NY 08:28 (straddles 08:30) -> no capture
        ctx.MacroOpen.Should().BeNull();

        ctx.Append(OpenAt(NyMacro0830Utc.AddMinutes(10), 1.0880m));             // opens NY 08:40 (gaps over 08:30) -> captures
        ctx.MacroOpen.Should().Be(1.0880m);
    }

    [Fact]
    public void Macro_open_is_captured_once_per_day_and_later_candles_do_not_re_capture()
    {
        var ctx = NewContext();
        ctx.Append(OpenAt(NyMidnightUtc, 1.0800m));
        ctx.Append(OpenAt(NyMacro0830Utc, 1.0875m));                            // capture at 08:30
        ctx.Append(OpenAt(NyMacro0830Utc.AddMinutes(30), 1.0890m));            // NY 09:00 -> no re-capture
        ctx.Append(OpenAt(NyMacro0830Utc.AddMinutes(90), 1.0895m));           // NY 10:00 -> no re-capture

        ctx.MacroOpen.Should().Be(1.0875m);
    }

    [Fact]
    public void Macro_open_resets_each_new_york_day()
    {
        var ctx = NewContext();
        ctx.Append(OpenAt(NyMidnightUtc, 1.0800m));
        ctx.Append(OpenAt(NyMacro0830Utc, 1.0875m));                            // day 1 capture
        ctx.MacroOpen.Should().Be(1.0875m);

        // Day 2 first candle crosses 00:00 NY -> macro clears until day 2 reaches 08:30.
        ctx.Append(OpenAt(NyMidnightUtc.AddDays(1), 1.0920m));                  // NY 00:00 on 2024-07-02
        ctx.MacroOpen.Should().BeNull();

        ctx.Append(OpenAt(NyMacro0830Utc.AddDays(1), 1.0930m));                 // NY 08:30 on 2024-07-02
        ctx.MacroOpen.Should().Be(1.0930m);
    }

    [Fact]
    public void The_first_candle_does_not_spuriously_reset_a_warm_macro_open()
    {
        // Parity with MidnightOpen: a first candle that already sits at/after 08:30 captures macro and does
        // not throw or reset session-scoped state on its own initialising append.
        var ctx = NewContext();

        ctx.Append(OpenAt(NyMacro0830Utc, 1.0875m));

        ctx.MacroOpen.Should().Be(1.0875m);   // captured on the very first candle (it is >= 08:30)
        ctx.MidnightOpen.Should().Be(1.0875m); // first candle also seeds the midnight reference
    }

    [Fact]
    public void Macro_open_is_captured_on_dst_spring_forward_at_the_edt_correct_instant()
    {
        // 2024-03-10: clocks jump 02:00 -> 03:00; the rest of the day is EDT (UTC-4), so 08:30 EDT = UTC 12:30.
        var ctx = NewContext();
        ctx.Append(OpenAt(new DateTimeOffset(2024, 3, 10, 12, 25, 0, TimeSpan.Zero), 1.0800m)); // NY 08:25
        ctx.MacroOpen.Should().BeNull();

        ctx.Append(OpenAt(new DateTimeOffset(2024, 3, 10, 12, 30, 0, TimeSpan.Zero), 1.0875m)); // NY 08:30 EDT
        ctx.MacroOpen.Should().Be(1.0875m);
    }

    [Fact]
    public void Macro_open_is_captured_on_dst_fall_back_at_the_est_correct_instant()
    {
        // 2024-11-03: clocks fall 02:00 -> 01:00; the rest of the day is EST (UTC-5), so 08:30 EST = UTC 13:30.
        var ctx = NewContext();
        ctx.Append(OpenAt(new DateTimeOffset(2024, 11, 3, 13, 25, 0, TimeSpan.Zero), 1.0800m)); // NY 08:25 EST
        ctx.MacroOpen.Should().BeNull();

        ctx.Append(OpenAt(new DateTimeOffset(2024, 11, 3, 13, 30, 0, TimeSpan.Zero), 1.0875m)); // NY 08:30 EST
        ctx.MacroOpen.Should().Be(1.0875m);
    }
}
