namespace IctTrader.Domain.Configuration;

/// <summary>
/// Which price inside the selected entry array the resting limit rests at (plan §2.5.1 step 7). Both are
/// resting limits — neither introduces a look-ahead/open-at-confirmation path; the difference is only HOW DEEP
/// into the gap the limit sits, which trades entry quality for fill probability.
/// </summary>
public enum EntryFillZone
{
    /// <summary>
    /// The canonical §2.5 deep Optimal-Trade-Entry: the selected fair-value-gap array level inside the 62–79%
    /// band (sweet spot 70.5%). The deepest, best-priced entry — but price often never retraces this far, so many
    /// setups never fill. The default → byte-identical to the established behaviour.
    /// </summary>
    Ote,

    /// <summary>
    /// Consequent Encroachment (CE) — the 50% midpoint of the selected entry fair-value gap. ICT's shallower,
    /// canonical alternative: price reaches the gap's 50% more often than the deep OTE retrace, so it converts
    /// more setups into real fills at a slightly worse entry (no look-ahead — still a resting limit). Provenance:
    /// CE is ICT-canon ("consequent encroachment") but Primer/community vs the §2.5 70.5% deep-OTE default.
    /// </summary>
    ConsequentEncroachment,

    /// <summary>
    /// The selected fair-value gap's NEAR (proximal) edge — the FIRST level price taps as it retraces into the
    /// gap (a bullish gap's top, a bearish gap's bottom). The SHALLOWEST of the three: it converts the most setups
    /// into real fills ("if price taps the gap instead of the 70.5% level, you can engage there" — ICT's documented
    /// backup) at the worst entry / lowest reward-to-risk, so the RR floor rejects the weakest. Still a resting
    /// limit (no look-ahead). Provenance: Primer/community "tap the gap" backup, NOT the §2.5 deep-OTE default.
    /// </summary>
    FvgNearEdge,
}
