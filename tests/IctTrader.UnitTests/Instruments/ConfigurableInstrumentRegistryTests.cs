using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Instruments;

/// <summary>
/// Locks the per-instrument config seam (`Ict:Instruments`): a baked per-pair tuning result (e.g. NAS100 → 6-of-8)
/// overlays the built-in catalog profile and reaches the scanner's confluence gate, while FX majors stay strict and
/// an explicit per-run override still wins.
/// </summary>
public sealed class ConfigurableInstrumentRegistryTests
{
    private static ConfigurableInstrumentRegistry RegistryWith(string symbol, int minRequired)
        => new(InstrumentCatalog.Default, new Dictionary<string, InstrumentOptionOverrides>
        {
            [symbol] = new() { MinRequiredConditions = minRequired },
        });

    [Fact]
    public void Config_override_merges_onto_the_catalog_profile_keeping_built_in_geometry()
    {
        var registry = RegistryWith("NAS100USD", 6);

        var profile = registry.Resolve(new Symbol("NAS100USD"));

        profile.Overrides.MinRequiredConditions.Should().Be(6);                  // from config
        profile.Overrides.MinStopDistancePips.Should().Be(10m);                  // built-in index geometry preserved
        profile.Overrides.SpreadBasePips.Should().Be(1.0m);
        profile.Overrides.UseMacroOpenReference.Should().BeTrue();
        profile.InstrumentClass.Should().Be(InstrumentClass.Index);
    }

    [Fact]
    public void A_symbol_with_no_config_passes_through_unchanged()
    {
        var registry = RegistryWith("NAS100USD", 6);

        var eurusd = registry.Resolve(new Symbol("EURUSD"));

        eurusd.Overrides.MinRequiredConditions.Should().BeNull();   // FX stays strict (the canonical default)
        eurusd.Overrides.Should().BeSameAs(InstrumentOptionOverrides.None);
    }

    [Fact]
    public void The_symbol_lookup_is_case_insensitive()
    {
        var registry = RegistryWith("nas100usd", 5); // lower-case config key

        registry.Resolve(new Symbol("NAS100USD")).Overrides.MinRequiredConditions.Should().Be(5);
    }

    [Fact]
    public void Confluence_applies_the_instrument_k_but_an_explicit_per_run_value_wins()
    {
        var nas100 = new InstrumentOptionOverrides { MinRequiredConditions = 6 };

        // No per-run override → the instrument's baked k applies.
        new ConfluenceOptions().WithInstrumentOverrides(nas100).MinRequiredConditions.Should().Be(6);

        // An explicit per-run override (e.g. an optimizer combo) WINS over the instrument default.
        new ConfluenceOptions { MinRequiredConditions = 5 }
            .WithInstrumentOverrides(nas100).MinRequiredConditions.Should().Be(5);

        // FX None leaves it strict (null), byte-identical.
        new ConfluenceOptions().WithInstrumentOverrides(InstrumentOptionOverrides.None)
            .MinRequiredConditions.Should().BeNull();
    }

    [Fact]
    public void Out_of_range_instrument_k_fails_validation()
    {
        new InstrumentOverridesOptions
        {
            Overrides = { ["NAS100USD"] = new() { MinRequiredConditions = 0 } },
        }.Validate().Should().NotBeEmpty();

        new InstrumentOverridesOptions
        {
            Overrides = { ["NAS100USD"] = new() { MinRequiredConditions = 6 } },
        }.Validate().Should().BeEmpty();
    }
}
