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
    /// The US index-CFD PRICE geometry (OANDA's <c>NAS100USD</c> / <c>SPX500USD</c> CFDs): one "pip" is one ICT
    /// HANDLE = 1.0 index point (DERIVED — the 2022 Mentorship is an index e-mini mentorship and teaches indices in
    /// handles where a handle = 1.00 point; Ep1 L214-216/L314-317), so every §2.5 pip-denominated threshold reads
    /// as points on the index. <see cref="TickSize"/> = 0.1 is the OANDA CFD tick (CONVENTION); note this diverges
    /// from ICT's NQ/ES FUTURES tick (0.25 NQ / 0.25 ES = one quarter handle), so a future tick-denominated rule must
    /// not assume 0.1 equals ICT's tick. <see cref="InstrumentClass.Index"/> routes session math to the §2.5.7 index
    /// killzone (AM 08:30–11:00) — the SOLE reason this factory exists. NASDAQ + S&amp;P share this CFD geometry.
    /// </summary>
    public static SymbolSpec Index(Symbol symbol) => new(symbol, 1.0m, 0.1m, 1, InstrumentClass.Index);

    /// <summary>NASDAQ-100 index geometry — an alias of <see cref="Index"/> (kept for existing call sites/tests).</summary>
    public static SymbolSpec Nas100(Symbol symbol) => Index(symbol);

    /// <summary>
    /// The spot-metal PRICE geometry (OANDA's <c>XAU_USD</c> gold CFD). A gold "pip" is 0.1 (ten cents) and the
    /// OANDA quote carries 2 decimals, so <see cref="TickSize"/> = 0.01 (CONVENTION — OANDA XAU_USD spec). Gold
    /// keeps <see cref="InstrumentClass.Fx"/> ON PURPOSE: ICT trades gold on the SAME London/NY FX sessions
    /// (gold is a USD-denominated spot vehicle, not a US-equity-index future), so it must route through
    /// <c>ClassifyFx</c>, not the index AM killzone — and no new <see cref="InstrumentClass"/> value is added
    /// (that would force an exhaustive-switch ripple for a vehicle that shares FX session math).
    ///
    /// <para><b>Provenance.</b> Gold does NOT appear in the 2022 NASDAQ e-mini Mentorship and is a SECONDARY,
    /// event-driven vehicle in the wider ICT material, so EVERY number here is CONVENTION (the OANDA XAU_USD CFD
    /// spec) / INVENTED — none is Mentorship-verbatim. The pip = 0.1 / tick = 0.01 / digits = 2 triple matches the
    /// OANDA gold quote and the common "$0.10 = 1 pip" convention, NOT an ICT-stated value.</para>
    /// </summary>
    public static SymbolSpec Metal(Symbol symbol) => new(symbol, 0.1m, 0.01m, 2, InstrumentClass.Fx);
}
