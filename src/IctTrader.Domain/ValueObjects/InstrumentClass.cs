namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// The instrument class an ICT killzone schedule is keyed on (plan §2.5.7 caveat 3). FX and index futures
/// use SEPARATE killzone windows (FX New-York 07:00–10:00 vs Index AM 08:30–11:00), so classification must
/// know which set applies. This project defaults to <see cref="Fx"/>.
/// </summary>
public enum InstrumentClass
{
    Fx,
    Index,
}
