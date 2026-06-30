using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Contracts;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// One Manual-mode confirmed setup awaiting an operator TAKE (plan §15). It is a thin holder over the wire
/// <see cref="SetupDto"/> plus the bits the store needs to decide expiry — the symbol's <see cref="InstrumentClass"/>
/// (FX vs Index → which §2.5.7 killzone schedule governs killzone-end) and the <see cref="DetectedAtUtc"/> the setup
/// confirmed (the age + killzone-classification anchor).
///
/// <para><b>Reserves nothing.</b> A pending books no P&amp;L and reserves no §2.5.10 risk — it is NOT a position, so it
/// lives only in memory and never enters the DB-as-state position model. Taking it routes through the SAME simulated
/// open as the automatic path (paper-only, §6.3).</para>
/// </summary>
internal sealed record PendingOpportunity(SetupDto Setup, InstrumentClass InstrumentClass)
{
    /// <summary>The deterministic seam id (the take command's key + the future trade's id).</summary>
    public Guid Id => Setup.Id;

    /// <summary>When the setup confirmed — both the age anchor and the killzone-classification instant.</summary>
    public DateTimeOffset DetectedAtUtc => Setup.DetectedAtUtc;
}
