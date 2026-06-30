using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.PaperTrading.Application;

namespace IctTrader.UnitTests.PaperTrading;

/// <summary>
/// Locks the TAKE-workflow options + the per-instrument resolution (plan §15): the POCO code default is <c>Auto</c>
/// (so code-constructed-options tests stay byte-identical), <see cref="PaperTradingOptions.EffectiveEntryMode"/> is a
/// pure per-instrument-override-else-global resolve, and <see cref="InstrumentOptionOverrides.OverlayWith"/> threads the
/// new <see cref="TradeEntryMode"/> through the merge. The <c>appsettings.json</c> Manual default is a config value, not
/// a POCO default — proven separately by the integration test that boots the real Host.
/// </summary>
public class TradeEntryModeOptionsTests
{
    [Fact]
    public void PaperTradingOptions_default_entry_mode_is_Auto_so_code_constructed_options_stay_byte_identical()
    {
        new PaperTradingOptions().DefaultEntryMode.Should().Be(TradeEntryMode.Auto);
    }

    [Fact]
    public void EffectiveEntryMode_falls_back_to_the_global_default_when_no_override()
    {
        var options = new PaperTradingOptions { DefaultEntryMode = TradeEntryMode.Manual };

        options.EffectiveEntryMode(null).Should().Be(TradeEntryMode.Manual);
        options.EffectiveEntryMode(InstrumentOptionOverrides.None).Should().Be(TradeEntryMode.Manual);
    }

    [Theory]
    [InlineData(TradeEntryMode.Auto, TradeEntryMode.Manual)]   // override Manual wins over a global Auto
    [InlineData(TradeEntryMode.Manual, TradeEntryMode.Auto)]   // override Auto wins over a global Manual
    public void EffectiveEntryMode_prefers_the_per_instrument_override(
        TradeEntryMode global, TradeEntryMode perInstrument)
    {
        var options = new PaperTradingOptions { DefaultEntryMode = global };
        var overrides = new InstrumentOptionOverrides { EntryMode = perInstrument };

        options.EffectiveEntryMode(overrides).Should().Be(perInstrument);
    }

    [Fact]
    public void Validate_rejects_an_undefined_entry_mode()
    {
        var options = new PaperTradingOptions { DefaultEntryMode = (TradeEntryMode)99 };

        options.Validate().Should().ContainSingle().Which.Should().Contain("DefaultEntryMode");
    }

    [Fact]
    public void OverlayWith_threads_the_entry_mode_with_other_winning_where_set()
    {
        var baseline = new InstrumentOptionOverrides { EntryMode = TradeEntryMode.Auto };
        var top = new InstrumentOptionOverrides { EntryMode = TradeEntryMode.Manual };

        baseline.OverlayWith(top).EntryMode.Should().Be(TradeEntryMode.Manual);
    }

    [Fact]
    public void OverlayWith_keeps_the_base_entry_mode_when_other_is_silent()
    {
        var baseline = new InstrumentOptionOverrides { EntryMode = TradeEntryMode.Manual };
        var top = new InstrumentOptionOverrides { MinRequiredConditions = 6 }; // EntryMode null

        baseline.OverlayWith(top).EntryMode.Should().Be(TradeEntryMode.Manual);
    }

    [Fact]
    public void PendingOpportunityOptions_defaults_are_valid_and_bounds_are_enforced()
    {
        new PendingOpportunityOptions().Validate().Should().BeEmpty();

        new PendingOpportunityOptions { MaxPendingMinutes = 0 }.Validate()
            .Should().ContainSingle().Which.Should().Contain("MaxPendingMinutes");
        new PendingOpportunityOptions { MaxPending = 0 }.Validate()
            .Should().ContainSingle().Which.Should().Contain("MaxPending");
    }
}
