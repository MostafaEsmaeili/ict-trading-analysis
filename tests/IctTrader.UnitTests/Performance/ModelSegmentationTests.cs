using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Services;
using IctTrader.Domain.Setups;
using IctTrader.Performance.Application;

namespace IctTrader.UnitTests.Performance;

/// <summary>
/// Locks the per-model performance segmentation (plan §16 D6): closes are tagged with the setup model that
/// produced the trade, the unfiltered snapshot stays the frozen all-trades behavior, and a model filter
/// narrows to that model's own close stream. Also locks the live active-model override seam on
/// <see cref="RuntimeSettings"/> (null/empty clears; every change bumps the revision).
/// </summary>
public sealed class ModelSegmentationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Snapshot_filters_by_model_and_null_returns_everything()
    {
        var state = new PerformanceState();
        state.Record(new ClosedTradeR(1.5m, T0), "Ict2022");
        state.Record(new ClosedTradeR(-1.0m, T0.AddMinutes(5)), "Ict2024");
        state.Record(new ClosedTradeR(2.0m, T0.AddMinutes(10)), "Ict2024");
        state.Record(new ClosedTradeR(0.5m, T0.AddMinutes(15))); // untagged (pre-multi-model)

        state.Snapshot().Should().HaveCount(4, "no filter = the frozen all-trades behavior");
        state.Snapshot("Ict2024").Select(c => c.R).Should().Equal(-1.0m, 2.0m);
        state.Snapshot("ict2022").Should().ContainSingle("the model filter matches case-insensitively");
        state.Models().Should().BeEquivalentTo(["Ict2022", "Ict2024"], "untagged closes name no model");
    }

    [Fact]
    public void Active_models_override_is_live_revisioned_and_never_empty()
    {
        var settings = new RuntimeSettings();
        settings.ActiveModelsOverride.Should().BeNull("no override set → the configured default applies");
        var before = settings.Revision;

        settings.SetActiveModels([SetupModel.Ict2024, SetupModel.Ict2024, SetupModel.Ict2022]);

        settings.ActiveModelsOverride.Should().Equal(SetupModel.Ict2024, SetupModel.Ict2022);
        settings.Revision.Should().BeGreaterThan(before, "the scanner caches rebuild on a revision tick");

        settings.SetActiveModels([]);
        settings.ActiveModelsOverride.Should().BeNull(
            "an empty selection clears the override — the operator can never select 'nothing' into a dead scanner");
    }
}
