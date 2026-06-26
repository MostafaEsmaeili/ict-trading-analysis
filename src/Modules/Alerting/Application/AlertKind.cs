namespace IctTrader.Alerting.Application;

/// <summary>
/// The stable <see cref="Contracts.AlertDto.Kind"/> discriminator strings the dashboard's Alerts feed renders
/// (icon / colour per kind). Centralised as named consts so the wire values can never drift between the
/// emitting handlers and are not scattered magic strings (project convention: no magic strings).
/// </summary>
internal static class AlertKind
{
    /// <summary>A confirmed, advisory ICT setup (from <c>Scanning.SetupConfirmed</c>).</summary>
    public const string Setup = "Setup";

    /// <summary>A simulated paper trade was opened (from <c>PaperTrading.PaperTradeOpened</c>).</summary>
    public const string TradeOpened = "TradeOpened";

    /// <summary>A simulated paper trade was closed (from <c>PaperTrading.PaperTradeClosed</c>).</summary>
    public const string TradeClosed = "TradeClosed";
}
