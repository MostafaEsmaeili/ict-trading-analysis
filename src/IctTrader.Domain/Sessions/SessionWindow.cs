using IctTrader.Domain.Common;

namespace IctTrader.Domain.Sessions;

/// <summary>
/// A New-York-local intraday window with <b>inclusive start, exclusive end</b> semantics (plan §2.5.5).
/// Windows that cross midnight (e.g. Asian 19:00–00:00) wrap: when <see cref="End"/> is at or before
/// <see cref="Start"/>, the window runs from Start to midnight and (for a true wrap) from midnight to End.
/// </summary>
public readonly record struct SessionWindow
{
    public SessionWindow(TimeOnly start, TimeOnly end)
    {
        Guard.Against(start == end, "SessionWindow start and end must differ.");
        Start = start;
        End = end;
    }

    public TimeOnly Start { get; }

    public TimeOnly End { get; }

    /// <summary>True if the New-York time-of-day falls in this window (start inclusive, end exclusive).</summary>
    public bool Contains(TimeOnly timeOfDay)
        => Start < End
            ? timeOfDay >= Start && timeOfDay < End
            : timeOfDay >= Start || timeOfDay < End;
}
