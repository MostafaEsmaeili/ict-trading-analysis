using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;
using Reqnroll;

namespace IctTrader.E2E.StepDefinitions;

/// <summary>
/// Drives the killzone-classification Scenario Outline (plan §8.3) directly against the pure domain
/// <see cref="KillzoneClock"/> — the DST-aware NY-session classifier. It proves the session boundaries are correct
/// in New-York wall-clock time via the IANA id <c>America/New_York</c> (the <see cref="NyClock"/> resolves it),
/// independent of the host's own zone. A new step-class instance is created per scenario, so the instance fields
/// are scenario-scoped state (no shared mutable global).
/// </summary>
[Binding]
public sealed class KillzoneClassificationSteps
{
    // A fixed summer trading day so the result is unambiguously under US Eastern Daylight Time (NY = UTC-4).
    private static readonly DateOnly SummerDay = new(2024, 7, 1);

    private static readonly KillzoneClock Clock =
        new(new NyClock(new FakeTimeProvider()), KillzoneSchedule.CreateDefault());

    private DateTimeOffset _candleOpenUtc;
    private KillzoneClassification _classification;

    [Given("an FX candle opening at \"(.*)\" New York time on a summer trading day")]
    public void GivenAnFxCandleOpeningAt(string nyTime)
    {
        var timeOfDay = TimeOnly.Parse(nyTime, System.Globalization.CultureInfo.InvariantCulture);
        _candleOpenUtc = ToUtcFromNewYork(SummerDay, timeOfDay);
    }

    [When("the killzone for that candle is evaluated")]
    public void WhenTheKillzoneIsEvaluated()
    {
        _classification = Clock.Classify(_candleOpenUtc, InstrumentClass.Fx);
    }

    [Then("the killzone should be classified as \"(.*)\"")]
    public void ThenTheKillzoneShouldBeClassifiedAs(string killzone)
    {
        _classification.Killzone.Should().Be(Enum.Parse<Killzone>(killzone));
    }

    /// <summary>Converts a New-York wall-clock time on a given day to the equivalent UTC instant (DST-aware),
    /// so the candle's stored UTC open time round-trips back to exactly that NY time through the clock.</summary>
    private static DateTimeOffset ToUtcFromNewYork(DateOnly day, TimeOnly timeOfDay)
    {
        var newYork = NyClock.ResolveNewYorkZone();
        var unspecified = new DateTime(day.Year, day.Month, day.Day, timeOfDay.Hour, timeOfDay.Minute, 0,
            DateTimeKind.Unspecified);
        var offset = newYork.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }
}
