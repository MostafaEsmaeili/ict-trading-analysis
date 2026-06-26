using System.Globalization;

namespace IctTrader.Alerting.Application;

/// <summary>
/// Central, deterministic templates for the human-readable <see cref="Contracts.AlertDto.Message"/> the
/// trade handlers compose (mirrors <c>Domain.Detection.ReasonFragments</c>, plan §4.5). Centralised here so
/// the handlers carry no inline literals; the full localisable <c>.resx</c> migration is a later
/// (Host/Resources) WP. Numbers/R are formatted with the invariant culture so the text is identical on any
/// host.
///
/// <para>The setup alert reuses the §2.5 reasoning verbatim (<c>setup.Reason</c>) and so needs no template.</para>
/// </summary>
internal static class AlertMessages
{
    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>e.g. <c>"Opened Long EURUSD @ 1.0850"</c>.</summary>
    public static string TradeOpened(string direction, string symbol, decimal entry)
        => $"Opened {direction} {symbol} @ {N(entry)}";

    /// <summary>
    /// e.g. <c>"Closed EURUSD TargetHit (+2.00R)"</c>. The R multiple uses a signed format so a win shows
    /// <c>+</c> and a loss <c>-</c> (a flat scratch shows <c>+0.00R</c>).
    /// </summary>
    public static string TradeClosed(string symbol, string outcome, decimal realizedR)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"Closed {symbol} {outcome} ({realizedR:+0.00;-0.00}R)");
}
