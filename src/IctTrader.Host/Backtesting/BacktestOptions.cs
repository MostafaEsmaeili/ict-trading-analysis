namespace IctTrader.Host.Backtesting;

/// <summary>
/// Where the on-demand backtest engine reads recorded-history CSVs from (bound from <c>Ict:Backtest</c>) — no magic
/// path. The files follow the fetch/replay convention <c>&lt;SYMBOL&gt;-&lt;TF&gt;.csv</c> (e.g. <c>EURUSD-M5.csv</c>),
/// the same format <see cref="IctTrader.MarketData.Infrastructure.Feeds.CsvCandleSource"/> reads and the OANDA history
/// fetch writes. Defaults to <c>data</c> (relative to the host's working directory) so the fetched datasets are found
/// out of the box. Read-only — the engine only loads these files; it never writes one.
/// </summary>
public sealed class BacktestOptions
{
    public const string SectionName = "Ict:Backtest";

    /// <summary>The directory holding the <c>&lt;SYMBOL&gt;-&lt;TF&gt;.csv</c> history datasets. Default <c>data</c>.</summary>
    public string DataDirectory { get; init; } = "data";
}
