namespace IctTrader.Domain.Sessions;

/// <summary>
/// ICT killzone classification, defined in New York time with US DST (plan §2.1/§2.5.5). FROZEN
/// CONTRACT (plan §11.1 #1/#5): member names are depended on by config
/// (<c>Ict:Scanning:ActiveKillzones</c>), DTOs, the Gherkin outline, and the dashboard.
/// <see cref="None"/> means the candle falls outside every killzone.
/// </summary>
public enum Killzone
{
    None,
    Asian,
    LondonOpen,
    NewYorkOpen,
    LondonClose,
}
