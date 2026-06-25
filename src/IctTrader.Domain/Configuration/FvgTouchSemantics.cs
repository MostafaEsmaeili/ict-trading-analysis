namespace IctTrader.Domain.Configuration;

/// <summary>
/// How a return into a fair-value gap counts toward the two-touch void (decision FVG-SEM-1a). The §2.5.8 / Ep38
/// reading is <see cref="WickInto"/> — the operative event is price TRADING back into the void, tested on the bar's
/// High/Low — kept as the default. <see cref="CloseInto"/> is the flagged alternative for operators who only count a
/// return once a bar CLOSES inside the gap.
/// </summary>
public enum FvgTouchSemantics
{
    /// <summary>A touch is any bar whose High/Low range trades into the gap (the Ep38 default).</summary>
    WickInto,

    /// <summary>A touch is only a bar that closes inside the gap.</summary>
    CloseInto,
}
