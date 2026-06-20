namespace IctTrader.Domain.Sessions;

/// <summary>
/// The New-York-local killzone window definitions (plan §2.5.5, reconciled by §2.5.10). These ARE the ICT
/// model — the canonical, citation-backed session boundaries — expressed as one cohesive value object so
/// the classification logic carries no inline numbers. <see cref="CreateDefault"/> supplies the verified
/// defaults; the Host may bind an override from <c>Ict:Killzones</c> config (added when the scanner is
/// wired). FX and index futures use SEPARATE windows (§2.5.7 caveat 3).
/// </summary>
public sealed class KillzoneSchedule
{
    public required SessionWindow LondonOpen { get; init; }

    public required SessionWindow NewYorkOpen { get; init; }

    public required SessionWindow LondonClose { get; init; }

    public required SessionWindow Pm { get; init; }

    public required SessionWindow IndexAm { get; init; }

    public required SessionWindow Asian { get; init; }

    /// <summary>The HARD no-trade lunch window, applied to BOTH instrument classes (§2.5.10 #4).</summary>
    public required SessionWindow Lunch { get; init; }

    /// <summary>Index AM advisory last-entry cutoff (~10:40 NY); past it the killzone blocks NEW entries only.</summary>
    public required TimeOnly IndexAmLastEntry { get; init; }

    /// <summary>
    /// The transcript-verified default windows (NY local). Conflicts between §2.1 and the entry model were
    /// resolved by §2.5.10 in favour of the entry model and are cited inline.
    /// </summary>
    public static KillzoneSchedule CreateDefault() => new()
    {
        LondonOpen = new SessionWindow(new TimeOnly(2, 0), new TimeOnly(5, 0)),       // §2.5.5
        NewYorkOpen = new SessionWindow(new TimeOnly(7, 0), new TimeOnly(10, 0)),     // §2.5.10 #6 (over §2.1 07:00-09:00)
        LondonClose = new SessionWindow(new TimeOnly(10, 0), new TimeOnly(11, 0)),    // §2.5.10 #3 (over §2.1 10:00-12:00)
        Pm = new SessionWindow(new TimeOnly(13, 30), new TimeOnly(16, 0)),            // §2.5.5
        IndexAm = new SessionWindow(new TimeOnly(8, 30), new TimeOnly(11, 0)),        // §2.5.5 (index class)
        Asian = new SessionWindow(new TimeOnly(19, 0), new TimeOnly(0, 0)),           // §2.5.10 #6 (19:00-00:00, wraps to midnight)
        Lunch = new SessionWindow(new TimeOnly(12, 0), new TimeOnly(13, 0)),          // §2.5.10 #4 (hard no-trade)
        IndexAmLastEntry = new TimeOnly(10, 40),                                      // §2.5.5 (advisory)
    };
}
