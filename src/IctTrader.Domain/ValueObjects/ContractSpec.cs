using IctTrader.Domain.Common;

namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// The per-instrument MONEY geometry used to size and book a paper trade (plan §5.1/§5.4) — the value of one
/// pip per standard lot, the lot step, and the minimum tradeable lot. This is intentionally separate from
/// <see cref="SymbolSpec"/> (which carries only price geometry — pip size, tick size, digits) so the
/// price-only detectors never depend on account-currency concerns. Conversions are stated PER 1.0 STANDARD LOT:
/// the money value of a price move is <c>pips × ValuePerPip × lots</c> (the unit convention the sizer and the
/// trade's P&amp;L booking both honour). Dynamic account-currency conversion for non-USD-quote/JPY pairs is a
/// later (§5.4) realism concern; a static configured value per symbol is the paper default.
/// </summary>
public sealed record ContractSpec
{
    public ContractSpec(Symbol symbol, decimal valuePerPip, decimal lotStep, decimal minLot)
    {
        Guard.Against(symbol is null, "ContractSpec requires a symbol.");
        Guard.Against(valuePerPip <= 0m, "ContractSpec value-per-pip must be positive.");
        Guard.Against(lotStep <= 0m, "ContractSpec lot step must be positive.");
        Guard.Against(minLot <= 0m, "ContractSpec minimum lot must be positive.");
        Guard.Against(minLot < lotStep, "ContractSpec minimum lot cannot be smaller than the lot step.");
        Symbol = symbol!;
        ValuePerPip = valuePerPip;
        LotStep = lotStep;
        MinLot = minLot;
    }

    public Symbol Symbol { get; }

    /// <summary>Account-currency value of one pip per 1.0 standard lot (EURUSD ≈ 10 USD, §5.4).</summary>
    public decimal ValuePerPip { get; }

    /// <summary>The smallest size increment the position rounds (floors) to.</summary>
    public decimal LotStep { get; }

    /// <summary>The smallest tradeable size; a sized position below this cannot open.</summary>
    public decimal MinLot { get; }

    /// <summary>A 5-digit FX major default — 10 USD/pip per lot, micro-lot (0.01) step and minimum (§5.4).</summary>
    public static ContractSpec FxMajor(Symbol symbol) => new(symbol, 10m, 0.01m, 0.01m);
}
