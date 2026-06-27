using IctTrader.PaperTrading.Contracts;
using IctTrader.Performance.Contracts;

namespace IctTrader.Host.Backtesting;

/// <summary>
/// An on-demand backtest request (plan §15): run the ICT model over one symbol's recorded history for a chosen
/// period, trade style, starting balance and per-trade risk, and read the result. <see cref="Timeframe"/> is
/// optional — it defaults to the style's entry timeframe (Scalp→M1, Intraday→M5, Swing→M15, Position→H4).
/// <see cref="FromUtc"/>/<see cref="ToUtc"/> are optional (default = the dataset's full range). The run is in-memory
/// and advisory only — it reuses the pure §2.5 domain, touches no broker, and persists nothing.
/// </summary>
public sealed record BacktestRequest(
    string Symbol,
    string Style,
    decimal StartingBalance,
    decimal RiskPercent,
    string? Timeframe = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int? MinRequiredConditions = null,
    IReadOnlyList<string>? RequiredConditions = null);

/// <summary>One point on a backtest equity curve: the account balance and the cumulative R at a trade's close.</summary>
public sealed record BacktestEquityPointDto(DateTimeOffset AtUtc, decimal Equity, decimal CumulativeR);

/// <summary>
/// The result of a backtest run: the echoed run parameters, headline counts, the R-based performance summary, the
/// account-balance + cumulative-R equity curve, and every trade (closed first, then any still open at the run end)
/// as the same <see cref="PaperTradeDto"/> the live trades table renders. Advisory only (§6.3).
/// </summary>
public sealed record BacktestResponse(
    string Symbol,
    string Timeframe,
    string Style,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    decimal StartingBalance,
    decimal RiskPercent,
    int? MinRequiredConditions,
    IReadOnlyList<string>? RequiredConditions,
    decimal EndingBalance,
    int CandlesProcessed,
    int SetupCount,
    int TradeCount,
    PerformanceSummaryDto Summary,
    IReadOnlyList<BacktestEquityPointDto> Equity,
    IReadOnlyList<PaperTradeDto> Trades);

/// <summary>A recorded-history dataset available to backtest: its symbol, timeframe, date range and candle count.</summary>
public sealed record BacktestDatasetDto(
    string Symbol,
    string Timeframe,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int CandleCount);
