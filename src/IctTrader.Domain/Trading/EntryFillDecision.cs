using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>What evaluating a resting entry limit against one bar produced (plan §2.5.1 step 7).</summary>
public enum EntryFillOutcome
{
    /// <summary>The bar did not retrace into the entry zone — the limit stays resting (unfilled).</summary>
    Hold,

    /// <summary>The bar traded into the entry level — the resting limit fills.</summary>
    Filled,
}

/// <summary>
/// The immutable result of evaluating one confirmed <see cref="Setups.Setup"/>'s resting entry limit against one
/// <see cref="Candle"/> (plan §2.5.1 step 7). ICT enters on a LIMIT at the OTE/FVG level — price must RETRACE into
/// the zone — so this DECIDES whether the bar touched it; the orchestrator APPLIES the fill (opens the
/// <see cref="PaperTrade"/>). Like the exit <see cref="FillDecision"/> it carries the resting fill price — always
/// the limit LEVEL (the plan entry), never the better gap price, so the planned 1R equals the booked 1R — and no
/// timestamp: the caller stamps the bar's close time, keeping the evaluator clock-free (§3.0/§4.8).
/// </summary>
public readonly record struct EntryFillDecision
{
    private EntryFillDecision(EntryFillOutcome outcome, Price? fillPrice)
    {
        Outcome = outcome;
        FillPrice = fillPrice;
    }

    /// <summary>The limit did not fill this bar — it stays resting.</summary>
    public static readonly EntryFillDecision Hold = new(EntryFillOutcome.Hold, null);

    /// <summary>A limit fill at the entry level.</summary>
    public static EntryFillDecision Filled(Price entryLevel)
        => new(EntryFillOutcome.Filled, entryLevel);

    public EntryFillOutcome Outcome { get; }

    /// <summary>The resting limit fill level (the plan entry); null when the limit did not fill this bar.</summary>
    public Price? FillPrice { get; }

    /// <summary>True when the bar filled the resting entry limit.</summary>
    public bool IsFilled => Outcome == EntryFillOutcome.Filled;
}
