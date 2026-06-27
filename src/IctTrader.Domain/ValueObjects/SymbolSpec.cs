using IctTrader.Domain.Common;

namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// The per-instrument contract specification (plan §5.4/§6.1) — pip size, tick size, digits, and class.
/// Every pip↔price conversion in the detectors goes through this, so no pip size is ever a literal. FX
/// majors are typically <c>PipSize 0.0001</c>; JPY pairs and index futures override.
/// </summary>
public sealed record SymbolSpec
{
    public SymbolSpec(Symbol symbol, decimal pipSize, decimal tickSize, int digits, InstrumentClass instrumentClass)
    {
        Guard.Against(symbol is null, "SymbolSpec requires a symbol.");
        Guard.Against(pipSize <= 0m, "SymbolSpec pip size must be positive.");
        Guard.Against(tickSize <= 0m, "SymbolSpec tick size must be positive.");
        Guard.Against(digits < 0, "SymbolSpec digits cannot be negative.");
        Symbol = symbol!;
        PipSize = pipSize;
        TickSize = tickSize;
        Digits = digits;
        InstrumentClass = instrumentClass;
    }

    public Symbol Symbol { get; }

    public decimal PipSize { get; }

    public decimal TickSize { get; }

    public int Digits { get; }

    public InstrumentClass InstrumentClass { get; }

    public decimal PipsToPrice(Pips pips) => pips.Value * PipSize;

    public Pips PriceToPips(decimal priceDistance) => new(Math.Abs(priceDistance) / PipSize);

    /// <summary>A 5-digit FX major default (pip 0.0001, tick 0.00001) — the project's primary instrument class.</summary>
    public static SymbolSpec FxMajor(Symbol symbol) => new(symbol, 0.0001m, 0.00001m, 5, InstrumentClass.Fx);

    /// <summary>
    /// The NASDAQ-100 index PRICE geometry (e.g. OANDA's <c>NAS100USD</c> CFD): one "pip" is one ICT HANDLE =
    /// 1.0 index point (DERIVED — the 2022 Mentorship is a NASDAQ e-mini index mentorship and teaches indices in
    /// handles where a handle = 1.00 point; Ep1 L214-216/L314-317), so every §2.5 pip-denominated threshold reads
    /// as points on the index. <see cref="TickSize"/> = 0.1 is the OANDA CFD tick (CONVENTION); note this diverges
    /// from ICT's NQ FUTURES tick of 0.25 (= one quarter handle), so a future tick-denominated rule must not assume
    /// 0.1 equals ICT's tick. <see cref="InstrumentClass.Index"/> routes session math to the §2.5.7 index killzone
    /// (AM 08:30–11:00) — the SOLE reason this factory exists.
    /// </summary>
    public static SymbolSpec Nas100(Symbol symbol) => new(symbol, 1.0m, 0.1m, 1, InstrumentClass.Index);
}
