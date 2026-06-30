namespace IctTrader.Domain.Configuration;

/// <summary>
/// WHO decides that a confirmed advisory setup becomes a (simulated) paper trade — the operator-facing "TAKE"
/// workflow switch (plan §15 / the operator's "give me the opportunity to use that setup"). This is ORTHOGONAL to
/// the existing <see cref="EntryMode"/> (Armed vs Immediate), which decides HOW a setup that IS being acted on opens
/// (rest a limit and wait for the §2.5.1-step-7 retrace, or open at the plan entry now). The two compose: a Manual
/// setup the operator takes still arms/opens through the SAME <see cref="EntryMode"/> path as an automatic one — so
/// the sizing, the §2.5.10 portfolio cap, and the paper-only guardrail are byte-identical between Auto and Take.
///
/// <para><b>Paper-only (plan §6.3 guardrail).</b> Neither value routes anywhere near an order path: both end at the
/// same simulated open (the <c>SetupTradeOpener</c> → <c>SetupRehydrator</c> → <c>TradeOrchestrator</c> →
/// <c>PaperTradeFactory</c> chain that only writes to our own aggregates). "Take" is a UI affordance over the
/// existing simulator, NOT a new execution path.</para>
/// </summary>
public enum TradeEntryMode
{
    /// <summary>The system opens the simulated paper trade automatically as soon as a setup confirms (the existing
    /// behaviour — the <c>SetupConfirmedHandler</c> arms/opens it). The POCO code default so every code-constructed
    /// options test stays byte-identical.</summary>
    Auto,

    /// <summary>The system records the confirmed setup as a PENDING opportunity and waits for the operator to TAKE it
    /// (the §15 semi-auto workflow). No paper trade is opened and no risk is reserved until a take command arrives;
    /// the pending expires by age or killzone-end if it is never taken.</summary>
    Manual,
}
