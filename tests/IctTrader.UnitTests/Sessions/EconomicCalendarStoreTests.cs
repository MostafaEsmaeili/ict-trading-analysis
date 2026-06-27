using FluentAssertions;
using IctTrader.Domain.Sessions;

namespace IctTrader.UnitTests.Sessions;

/// <summary>Locks the economic-calendar store contract: empty + unloaded until the first load, then a revision-bumping
/// copy-on-write snapshot the scanner watches (slice 3).</summary>
public sealed class EconomicCalendarStoreTests
{
    [Fact]
    public void Starts_empty_unloaded_at_revision_zero()
    {
        var store = new EconomicCalendarStore();

        store.IsLoaded.Should().BeFalse();
        store.Events.Should().BeEmpty();
        store.Revision.Should().Be(0);
    }

    [Fact]
    public void Load_publishes_a_snapshot_marks_loaded_and_bumps_the_revision()
    {
        var store = new EconomicCalendarStore();

        store.Load([new EconomicEvent(new DateOnly(2024, 1, 31), CalendarEventType.Fomc)]);

        store.IsLoaded.Should().BeTrue();
        store.Revision.Should().Be(1);
        store.Events.Should().ContainSingle(e => e.Type == CalendarEventType.Fomc);
    }

    [Fact]
    public void Each_load_replaces_the_set_and_ticks_the_revision()
    {
        var store = new EconomicCalendarStore();
        store.Load([new EconomicEvent(new DateOnly(2024, 1, 31), CalendarEventType.Fomc)]);

        store.Load(
        [
            new EconomicEvent(new DateOnly(2024, 2, 2), CalendarEventType.Nfp),
            new EconomicEvent(new DateOnly(2024, 2, 13), CalendarEventType.Cpi),
        ]);

        store.Revision.Should().Be(2);
        store.Events.Should().HaveCount(2);
        store.Events.Should().NotContain(e => e.Type == CalendarEventType.Fomc); // prior set replaced
    }

    [Fact]
    public void Events_is_an_immutable_snapshot_decoupled_from_the_source_enumerable()
    {
        var store = new EconomicCalendarStore();
        var source = new List<EconomicEvent> { new(new DateOnly(2024, 1, 31), CalendarEventType.Fomc) };

        store.Load(source);
        source.Add(new EconomicEvent(new DateOnly(2024, 2, 2), CalendarEventType.Nfp)); // mutate after load

        store.Events.Should().ContainSingle(); // the store kept its own copy
    }
}
