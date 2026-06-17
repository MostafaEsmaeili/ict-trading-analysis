using IctTrader.Domain.Common;

namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// A bid/ask quote at an instant. FROZEN CONTRACT (plan §11.1 #2): <see cref="TimeUtc"/> is ALWAYS UTC.
/// The spread carried here feeds the realistic execution-cost model (plan §5.4).
/// </summary>
public readonly record struct Tick
{
    public Tick(Symbol symbol, DateTimeOffset timeUtc, decimal bid, decimal ask)
    {
        Guard.Against(timeUtc.Offset != TimeSpan.Zero, "Tick.TimeUtc must be UTC (zero offset).");
        Guard.Against(bid <= 0m || ask <= 0m, "Tick bid/ask must be positive.");
        Guard.Against(ask < bid, "Tick ask must be >= bid.");

        Symbol = symbol;
        TimeUtc = timeUtc;
        Bid = bid;
        Ask = ask;
    }

    public Symbol Symbol { get; }

    public DateTimeOffset TimeUtc { get; }

    public decimal Bid { get; }

    public decimal Ask { get; }

    public decimal Mid => (Bid + Ask) / 2m;

    public decimal SpreadAbsolute => Ask - Bid;
}
