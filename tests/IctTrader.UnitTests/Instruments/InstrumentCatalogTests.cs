using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Instruments;

/// <summary>
/// Locks the per-instrument-class resolution (plan §2.5.7 caveat 3): the <see cref="InstrumentCatalog"/> maps
/// <c>EURUSD</c> to the existing FX-major geometry (no overrides → byte-identical to the prior hardcoded
/// <c>SymbolSpec.FxMajor</c>) and <c>NAS100USD</c> to the NASDAQ-100 index profile, so an index symbol carries
/// <see cref="InstrumentClass.Index"/> and the <see cref="KillzoneClock"/> routes it to the §2.5.7 index AM
/// killzone — proving the class switch activates THROUGH the catalog. It also pins the index point geometry, the
/// point-based sizing/cost, and the FX-unchanged regression.
/// </summary>
public class InstrumentCatalogTests
{
    private static readonly InstrumentCatalog Catalog = InstrumentCatalog.Default;
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly Symbol Nas100 = new("NAS100USD");

    // ---- Catalog resolution ----------------------------------------------------------------------------------

    [Fact]
    public void Eurusd_resolves_to_the_fx_major_profile_unchanged()
    {
        var profile = Catalog.Resolve(Eurusd);

        profile.InstrumentClass.Should().Be(InstrumentClass.Fx);
        profile.IsKnown.Should().BeTrue();

        // Byte-identical to the prior hardcoded FX-major path (the FX regression assertion).
        profile.SymbolSpec.Should().Be(SymbolSpec.FxMajor(Eurusd));
        profile.ContractSpec.Should().Be(ContractSpec.FxMajor(Eurusd));
        profile.SymbolSpec.PipSize.Should().Be(0.0001m);
        profile.ContractSpec.ValuePerPip.Should().Be(10m);

        // FX carries NO overrides → every WithInstrumentOverrides is a field-equal no-op.
        profile.Overrides.Should().Be(InstrumentOptionOverrides.None);
    }

    [Fact]
    public void Nas100_resolves_to_the_index_profile_with_point_geometry()
    {
        var profile = Catalog.Resolve(Nas100);

        profile.InstrumentClass.Should().Be(InstrumentClass.Index);
        profile.IsKnown.Should().BeTrue();

        // Price geometry: 1 pip = 1 index point (a handle).
        profile.SymbolSpec.PipSize.Should().Be(1.0m);
        profile.SymbolSpec.TickSize.Should().Be(0.1m);
        profile.SymbolSpec.Digits.Should().Be(1);
        profile.SymbolSpec.InstrumentClass.Should().Be(InstrumentClass.Index);

        // Money geometry: $1 per point per unit, 1-unit lots — NOT the FX 10/pip + 0.01 step.
        profile.ContractSpec.ValuePerPip.Should().Be(1m);
        profile.ContractSpec.LotStep.Should().Be(1m);
        profile.ContractSpec.MinLot.Should().Be(1m);
    }

    [Fact]
    public void Spx500_resolves_to_the_same_index_profile_as_nas100()
    {
        // ES (SPX500USD) is the 2022 Mentorship's co-primary index — it shares the OANDA CFD point geometry + the
        // §2.5.7 index killzone + the 08:30 macro reference with NAS100. The catalog recognises the index SET, so the
        // profile (class + price/money geometry + overrides) is field-equal to NAS100's apart from the symbol.
        var profile = Catalog.Resolve(new Symbol("SPX500USD"));

        profile.InstrumentClass.Should().Be(InstrumentClass.Index);
        profile.IsKnown.Should().BeTrue();
        profile.SymbolSpec.PipSize.Should().Be(1.0m);
        profile.SymbolSpec.TickSize.Should().Be(0.1m);
        profile.ContractSpec.ValuePerPip.Should().Be(1m);
        profile.Overrides.Should().Be(Catalog.Resolve(Nas100).Overrides); // same index overrides (macro ref, point costs)
    }

    [Fact]
    public void Known_symbols_include_both_index_vehicles_and_the_fx_majors()
    {
        InstrumentCatalog.KnownSymbols.Should().Contain("NAS100USD").And.Contain("SPX500USD")
            .And.Contain("EURUSD").And.Contain("USDJPY");
    }

    [Fact]
    public void An_unknown_symbol_falls_back_to_fx_default_but_is_flagged_not_known()
    {
        var profile = Catalog.Resolve(new Symbol("ZZZZZZ"));

        // Preserves today's behaviour (FX-major geometry, no overrides) so an uncatalogued symbol scans as before,
        // but IsKnown=false lets a caller log the fallback.
        profile.InstrumentClass.Should().Be(InstrumentClass.Fx);
        profile.IsKnown.Should().BeFalse();
        profile.SymbolSpec.Should().Be(SymbolSpec.FxMajor(new Symbol("ZZZZZZ")));
        profile.Overrides.Should().Be(InstrumentOptionOverrides.None);
    }

    [Fact]
    public void Symbol_lookup_is_case_and_whitespace_insensitive_via_symbol_normalisation()
    {
        // Symbol normalises to trimmed upper-invariant, so a lower/padded form still hits the index profile.
        Catalog.Resolve(new Symbol("  nas100usd ")).InstrumentClass.Should().Be(InstrumentClass.Index);
    }

    // ---- The class switch activates the index killzone THROUGH the catalog ------------------------------------

    [Fact]
    public void A_nas100_candle_at_0845_ny_classifies_as_the_index_am_killzone()
    {
        // 2024-07-01 is EDT (UTC-4): 08:45 NY = 12:45 UTC. The catalog gives Index, so KillzoneClock routes to
        // ClassifyIndex (AM 08:30–11:00) — an FX symbol at the same instant would be NewYorkOpen, proving the switch.
        var clock = NewClock();
        var at0845Ny = new DateTimeOffset(2024, 7, 1, 12, 45, 0, TimeSpan.Zero);

        var indexClass = Catalog.Resolve(Nas100).InstrumentClass;
        clock.Classify(at0845Ny, indexClass).Killzone.Should().Be(Killzone.Am);
    }

    [Fact]
    public void A_nas100_candle_at_0730_ny_is_not_an_fx_new_york_killzone_for_the_index()
    {
        // 07:30 NY = 11:30 UTC. For the INDEX this is before the AM window (08:30) → None. For an FX major the same
        // instant is NewYorkOpen (07:00–10:00) — the load-bearing contrast that proves the instrument-class switch.
        var clock = NewClock();
        var at0730Ny = new DateTimeOffset(2024, 7, 1, 11, 30, 0, TimeSpan.Zero);

        clock.Classify(at0730Ny, Catalog.Resolve(Nas100).InstrumentClass).Killzone.Should().Be(Killzone.None);
        clock.Classify(at0730Ny, Catalog.Resolve(Eurusd).InstrumentClass).Killzone.Should().Be(Killzone.NewYorkOpen);
    }

    // ---- Index sizing + cost use POINT geometry, not FX 10/pip ------------------------------------------------

    [Fact]
    public void A_nas100_trade_sizes_with_point_geometry()
    {
        var profile = Catalog.Resolve(Nas100);

        // Bullish plan: entry 18000, stop 17950 → 50-point (= 50-"pip") risk. 1% of 100,000 = 1,000 risk.
        // moneyPerLot = 50 points * 1.0/point = 50; 1,000 / 50 = 20 units (1-unit lot step). RiskBudget = 1,000.
        var plan = new TradePlan(
            Direction.Bullish, new Price(18_000m), new Price(17_950m),
            new TargetLadder(Direction.Bullish, new Price(18_050m), new Price(18_150m)));

        var sizing = PositionSizer.Size(
            new Money(100_000m), new RiskPercent(1.0m), plan, profile.SymbolSpec, profile.ContractSpec,
            new Pips(IndexMinStopPoints));

        sizing.StopDistance.Value.Should().Be(50m); // 50 points, not 500,000 FX pips
        sizing.Size.Lots.Should().Be(20m);          // 20 CFD units
        sizing.RiskBudget.Amount.Should().Be(1_000m);
    }

    [Fact]
    public void A_nas100_trade_books_point_based_spread_and_zero_commission()
    {
        var profile = Catalog.Resolve(Nas100);
        var costs = new ExecutionCostOptions().WithInstrumentOverrides(profile.Overrides);

        // The index overrides: 1.0-point spread, 0 commission (vs the FX 0.7-pip / 6.0-per-lot defaults).
        costs.Spread.BasePips.Should().Be(1.0m);
        costs.Commission.PerLotRoundTripUsd.Should().Be(0m);

        var model = new ExecutionCostModel(costs);
        var trade = new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Nas100, TradeStyle.Intraday, Timeframe.M5,
            new TradePlan(
                Direction.Bullish, new Price(18_000m), new Price(17_950m),
                new TargetLadder(Direction.Bullish, new Price(18_050m), new Price(18_150m))),
            new PositionSize(20m), pipSize: 1.0m, valuePerPip: 1.0m,
            new DateTimeOffset(2024, 7, 1, 12, 45, 0, TimeSpan.Zero));

        var result = model.Compute(trade);
        // valuePerPip for the position = 1.0 * 20 = 20/point. Spread = 2 legs * 1.0 point * 20 = 40. Commission = 0.
        result.SpreadCost.Amount.Should().Be(40m);
        result.Commission.Amount.Should().Be(0m);
        result.Total.Amount.Should().Be(40m);
    }

    // ---- FX path stays byte-identical when the (None) overrides are applied -----------------------------------

    [Fact]
    public void Applying_the_fx_none_overrides_leaves_every_option_field_equal()
    {
        var none = InstrumentOptionOverrides.None;

        new MarketContextOptions().WithInstrumentOverrides(none).UseMacroOpenReference.Should().BeFalse();
        new FvgOptions().WithInstrumentOverrides(none).MinGapPips.Should().Be(new FvgOptions().MinGapPips);
        new LiquidityOptions().WithInstrumentOverrides(none).EqualLevelTolerancePips
            .Should().Be(new LiquidityOptions().EqualLevelTolerancePips);
        new DrawOnLiquidityOptions().WithInstrumentOverrides(none).StopBufferPips
            .Should().Be(new DrawOnLiquidityOptions().StopBufferPips);
        new RiskOptions().WithInstrumentOverrides(none).MinStopDistancePips
            .Should().Be(new RiskOptions().MinStopDistancePips);
        new ExecutionCostOptions().WithInstrumentOverrides(none).Spread.BasePips
            .Should().Be(new ExecutionCostOptions().Spread.BasePips);
        new EntryManagementOptions().WithInstrumentOverrides(none).CloseProximityTolerancePips
            .Should().Be(new EntryManagementOptions().CloseProximityTolerancePips);
    }

    [Fact]
    public void The_index_overrides_enable_the_macro_reference_and_re_default_the_geometry()
    {
        var overrides = Catalog.Resolve(Nas100).Overrides;

        // TIME-10 (CONTESTED-~80%) resolution: the index turns the 08:30 macro Judas reference ON; FX stays off.
        new MarketContextOptions().WithInstrumentOverrides(overrides).UseMacroOpenReference.Should().BeTrue();

        // The mis-scaling absolute FX-pip floors are re-defaulted to index points (see InstrumentCatalog provenance).
        new FvgOptions().WithInstrumentOverrides(overrides).MinGapPips.Should().Be(0m);          // ATR carries the floor
        new FvgOptions().WithInstrumentOverrides(overrides).StackProximityPips.Should().Be(10m);
        new LiquidityOptions().WithInstrumentOverrides(overrides).EqualLevelTolerancePips.Should().Be(2m);
        new DrawOnLiquidityOptions().WithInstrumentOverrides(overrides).StopBufferPips.Should().Be(2m);
        new DrawOnLiquidityOptions().WithInstrumentOverrides(overrides).SweptLevelExclusionPips.Should().Be(2m);
        new RiskOptions().WithInstrumentOverrides(overrides).MinStopDistancePips.Should().Be(10m);

        // SweepMinPenetration (DERIVED, ≈2 ticks) is NOT re-defaulted — it stays at the global value for the index.
        new LiquidityOptions().WithInstrumentOverrides(overrides).SweepMinPenetrationPips
            .Should().Be(new LiquidityOptions().SweepMinPenetrationPips);
    }

    private const decimal IndexMinStopPoints = 10m;

    private static KillzoneClock NewClock()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero));
        return new KillzoneClock(new NyClock(time), KillzoneSchedule.CreateDefault());
    }
}
