using FluentAssertions;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Sessions;

/// <summary>
/// Locks the §2.5.5/§2.5.10 killzone windows: inclusive-start/exclusive-end boundaries, the HARD lunch
/// override, the FX-vs-index instrument-class split, the index-AM last-entry cutoff, the Asian wrap to
/// midnight, and DST/host-zone independence (plan §4.8). Instants are given in UTC with the equivalent NY
/// wall-clock noted (summer is EDT = UTC−4, winter is EST = UTC−5).
/// </summary>
public class KillzoneClockTests
{
    private static readonly KillzoneClock Clock = new(new NyClock(TimeProvider.System), KillzoneSchedule.CreateDefault());

    [Theory]
    // ---- FX, summer 2024-07-01 (EDT, UTC-4): NY = UTC - 4 ----
    [InlineData(2024, 7, 1, 7, 0, InstrumentClass.Fx, Killzone.LondonOpen, false, false)]   // NY 03:00
    [InlineData(2024, 7, 1, 8, 59, InstrumentClass.Fx, Killzone.LondonOpen, false, false)]  // NY 04:59
    [InlineData(2024, 7, 1, 9, 0, InstrumentClass.Fx, Killzone.None, false, false)]         // NY 05:00 (LO end exclusive)
    [InlineData(2024, 7, 1, 10, 59, InstrumentClass.Fx, Killzone.None, false, false)]       // NY 06:59 (dead time)
    [InlineData(2024, 7, 1, 11, 0, InstrumentClass.Fx, Killzone.NewYorkOpen, false, false)] // NY 07:00
    [InlineData(2024, 7, 1, 13, 59, InstrumentClass.Fx, Killzone.NewYorkOpen, false, false)]// NY 09:59
    [InlineData(2024, 7, 1, 14, 0, InstrumentClass.Fx, Killzone.LondonClose, false, false)] // NY 10:00 (hands NY->LC)
    [InlineData(2024, 7, 1, 14, 59, InstrumentClass.Fx, Killzone.LondonClose, false, false)]// NY 10:59
    [InlineData(2024, 7, 1, 15, 0, InstrumentClass.Fx, Killzone.None, false, false)]        // NY 11:00 (LC end exclusive)
    [InlineData(2024, 7, 1, 16, 30, InstrumentClass.Fx, Killzone.None, true, false)]        // NY 12:30 lunch (hard)
    [InlineData(2024, 7, 1, 17, 0, InstrumentClass.Fx, Killzone.None, false, false)]        // NY 13:00 (lunch end excl, pre-PM)
    [InlineData(2024, 7, 1, 17, 30, InstrumentClass.Fx, Killzone.Pm, false, false)]         // NY 13:30
    [InlineData(2024, 7, 1, 19, 59, InstrumentClass.Fx, Killzone.Pm, false, false)]         // NY 15:59
    [InlineData(2024, 7, 1, 20, 0, InstrumentClass.Fx, Killzone.None, false, false)]        // NY 16:00 (PM end exclusive)
    [InlineData(2024, 7, 1, 23, 0, InstrumentClass.Fx, Killzone.Asian, false, false)]       // NY 19:00
    // ---- FX winter 2024-01-01 (EST, UTC-5): NY 03:00 must still be LondonOpen (DST handled) ----
    [InlineData(2024, 1, 1, 8, 0, InstrumentClass.Fx, Killzone.LondonOpen, false, false)]   // NY 03:00
    // ---- Index, summer 2024-07-01 ----
    [InlineData(2024, 7, 1, 12, 0, InstrumentClass.Index, Killzone.None, false, false)]     // NY 08:00 (pre-AM)
    [InlineData(2024, 7, 1, 12, 30, InstrumentClass.Index, Killzone.Am, false, false)]      // NY 08:30
    [InlineData(2024, 7, 1, 14, 39, InstrumentClass.Index, Killzone.Am, false, false)]      // NY 10:39
    [InlineData(2024, 7, 1, 14, 40, InstrumentClass.Index, Killzone.Am, false, true)]       // NY 10:40 (last-entry cutoff)
    [InlineData(2024, 7, 1, 14, 59, InstrumentClass.Index, Killzone.Am, false, true)]       // NY 10:59
    [InlineData(2024, 7, 1, 15, 0, InstrumentClass.Index, Killzone.None, false, false)]     // NY 11:00 (AM end exclusive)
    [InlineData(2024, 7, 1, 16, 30, InstrumentClass.Index, Killzone.None, true, false)]     // NY 12:30 lunch (also Index)
    public void Classifies_instants_into_the_verified_windows(
        int year, int month, int day, int hour, int minute,
        InstrumentClass instrumentClass, Killzone expected, bool lunchBlocked, bool noNewEntry)
    {
        var utc = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

        var classification = Clock.Classify(utc, instrumentClass);

        classification.Killzone.Should().Be(expected);
        classification.LunchBlocked.Should().Be(lunchBlocked);
        classification.NoNewEntry.Should().Be(noNewEntry);
    }

    [Fact]
    public void Asian_wraps_to_midnight_but_excludes_the_new_day()
    {
        // NY 23:30 on 2024-07-01 (EDT) = 03:30 UTC on 2024-07-02 -> still Asian.
        var lateAsian = new DateTimeOffset(2024, 7, 2, 3, 30, 0, TimeSpan.Zero);
        // NY 00:30 = 04:30 UTC -> a new financial day, no killzone.
        var afterMidnight = new DateTimeOffset(2024, 7, 2, 4, 30, 0, TimeSpan.Zero);

        Clock.Classify(lateAsian, InstrumentClass.Fx).Killzone.Should().Be(Killzone.Asian);
        Clock.Classify(afterMidnight, InstrumentClass.Fx).Killzone.Should().Be(Killzone.None);
    }

    [Fact]
    public void Classification_is_identical_regardless_of_the_instants_offset()
    {
        // Same instant as NY 03:00 EDT, expressed in a Tokyo offset, must still classify as LondonOpen.
        var utc = new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
        var sameInstantInTokyo = utc.ToOffset(TimeSpan.FromHours(9));

        Clock.Classify(sameInstantInTokyo, InstrumentClass.Fx).Should().Be(Clock.Classify(utc, InstrumentClass.Fx));
    }

    [Fact]
    public void Is_active_entry_respects_the_operator_selected_set()
    {
        var londonOpenInstant = new DateTimeOffset(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);  // NY 03:00
        var pmInstant = new DateTimeOffset(2024, 7, 1, 17, 30, 0, TimeSpan.Zero);        // NY 13:30
        Killzone[] active = [Killzone.LondonOpen, Killzone.NewYorkOpen];

        Clock.IsActiveEntry(londonOpenInstant, InstrumentClass.Fx, active).Should().BeTrue();
        Clock.IsActiveEntry(pmInstant, InstrumentClass.Fx, active).Should().BeFalse();
    }

    [Fact]
    public void Index_am_after_last_entry_is_not_a_tradeable_entry()
    {
        var afterCutoff = new DateTimeOffset(2024, 7, 1, 14, 40, 0, TimeSpan.Zero); // NY 10:40
        Killzone[] active = [Killzone.Am, Killzone.Asian];

        Clock.IsActiveEntry(afterCutoff, InstrumentClass.Index, active).Should().BeFalse();
    }
}
