using Microsoft.AspNetCore.SignalR;

namespace IctTrader.Host.Hubs;

/// <summary>
/// Real-time push to the dashboard (plan §9). FROZEN CONTRACT (plan §11.1 #6): the route and the
/// client-method names. The hub is push-only — there is deliberately NO client-callable
/// "execute"/"order" method, so the defensive guardrail holds at the transport layer too (plan §6.3).
/// </summary>
public sealed class TradingHub : Hub
{
    public const string Route = "/hubs/trading";

    // Names of the client-side handlers the server invokes (no inbound trading methods exist).
    public const string SetupDetected = nameof(SetupDetected);
    public const string TradeUpdated = nameof(TradeUpdated);
    public const string PerformanceUpdated = nameof(PerformanceUpdated);
    public const string CandleAppended = nameof(CandleAppended);
}
