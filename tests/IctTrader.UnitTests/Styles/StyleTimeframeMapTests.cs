using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Styles;

/// <summary>
/// Locks <see cref="StyleTimeframeMap"/> — the pure matrix router that scans every active style on its canonical
/// ENTRY timeframe (plan §4.7). It must map a delivered candle's timeframe to exactly the active styles whose
/// entry TF is that timeframe, honour the active-set membership, and return nothing for a timeframe no active
/// style enters on. Built over the default <c>Ict:TradeStyles</c> policy (Scalp→M1, Intraday→M5, Swing→M15,
/// Position→H4) via the <see cref="TradeStyleClassifier"/> — no hardcoded TF.
/// </summary>
public sealed class StyleTimeframeMapTests
{
    private static readonly StyleTimeframeMap Map = new(new TradeStyleClassifier(new TradeStyleOptions()));

    private static readonly TradeStyle[] AllStyles =
        [TradeStyle.Scalp, TradeStyle.Intraday, TradeStyle.Swing, TradeStyle.Position];

    [Theory]
    [InlineData(Timeframe.M1, TradeStyle.Scalp)]
    [InlineData(Timeframe.M5, TradeStyle.Intraday)]
    [InlineData(Timeframe.M15, TradeStyle.Swing)]
    [InlineData(Timeframe.H4, TradeStyle.Position)]
    public void Maps_each_entry_timeframe_to_its_style(Timeframe timeframe, TradeStyle expected)
    {
        Map.StylesFor(timeframe, AllStyles).Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public void A_timeframe_no_active_style_enters_on_yields_empty()
    {
        // H1 is no style's entry TF under the default policy, so a delivered H1 candle scans nothing.
        Map.StylesFor(Timeframe.H1, AllStyles).Should().BeEmpty();
    }

    [Fact]
    public void Respects_the_active_set_membership()
    {
        // Intraday's entry TF is M5; if Intraday is not active, an M5 candle maps to no style.
        Map.StylesFor(Timeframe.M5, [TradeStyle.Scalp, TradeStyle.Swing]).Should().BeEmpty();
        // ...and an M1 candle still maps to Scalp from that same active set.
        Map.StylesFor(Timeframe.M1, [TradeStyle.Scalp, TradeStyle.Swing])
            .Should().ContainSingle().Which.Should().Be(TradeStyle.Scalp);
    }

    [Fact]
    public void An_empty_active_set_yields_empty_for_any_timeframe()
    {
        Map.StylesFor(Timeframe.M5, []).Should().BeEmpty();
    }
}
