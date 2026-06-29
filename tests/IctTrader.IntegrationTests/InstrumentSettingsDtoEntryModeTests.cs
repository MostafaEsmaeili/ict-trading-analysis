using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Host;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Locks the per-instrument TAKE-workflow knob on the live Settings wire (plan §15): <see cref="InstrumentSettingsDto"/>
/// carries the 3-state <c>EntryMode</c> (null = inherit) and maps it both ways, validating the enum member. This is the
/// wire the redesigned Settings page toggles Auto/Manual per instrument over the revision-stamped runtime store. Pure
/// DTO mapping — no DI/Docker.
/// </summary>
public sealed class InstrumentSettingsDtoEntryModeTests
{
    [Fact]
    public void Maps_a_manual_entry_mode_to_the_domain_override()
    {
        var dto = new InstrumentSettingsDto(EntryMode: "Manual");

        dto.ToOverrides().EntryMode.Should().Be(TradeEntryMode.Manual);
    }

    [Fact]
    public void A_null_entry_mode_means_inherit_the_global_default()
    {
        var dto = new InstrumentSettingsDto(EntryMode: null);

        dto.ToOverrides().EntryMode.Should().BeNull("a null entry mode inherits the global default");
    }

    [Fact]
    public void From_projects_the_domain_entry_mode_back_to_the_wire_member_name()
    {
        var overrides = new InstrumentOptionOverrides { EntryMode = TradeEntryMode.Manual };

        InstrumentSettingsDto.From(overrides).EntryMode.Should().Be("Manual");
    }

    [Fact]
    public void Round_trips_an_entry_mode_through_the_wire_and_back()
    {
        var overrides = new InstrumentOptionOverrides { EntryMode = TradeEntryMode.Auto };

        InstrumentSettingsDto.From(overrides).ToOverrides().EntryMode.Should().Be(TradeEntryMode.Auto);
    }

    [Theory]
    [InlineData("Sideways")] // not a member name
    [InlineData("99")]       // a numeric (Enum.IsDefined rejects it)
    public void Rejects_an_unknown_entry_mode(string value)
    {
        var dto = new InstrumentSettingsDto(EntryMode: value);

        var map = () => dto.ToOverrides();

        map.Should().Throw<ArgumentException>().WithMessage($"*{value}*");
    }
}
