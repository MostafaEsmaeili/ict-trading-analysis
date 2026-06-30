using IctTrader.Scanning.Contracts;

namespace IctTrader.Scanning.Application.Signals;

/// <summary>
/// A consumer-owned PORT (plan §3.0a) the Scanning signals feed uses to enrich each ranked signal with the semi-auto
/// TAKE state (plan §15) WITHOUT referencing the PaperTrading module's internals. The take state lives in PaperTrading
/// (the effective Auto/Manual entry mode, the in-memory pending board, whether a trade already exists for the id), so
/// the Host wires a PaperTrading-backed adapter; Scanning depends only on this abstraction. A no-op default
/// (<see cref="None"/>) leaves every signal in its takeable-unknown wire default, so a standalone Scanning module /
/// test resolves without PaperTrading.
///
/// <para>Read-only/advisory (§6.3): it reports DISPLAY state only — it never opens anything (a TAKE goes through the
/// PaperTrading <c>TakeSetupCommand</c>).</para>
/// </summary>
public interface ISignalTakeStateProvider
{
    /// <summary>The take-state for one confirmed advisory <paramref name="setup"/> as of <paramref name="nowUtc"/> — the
    /// effective entry mode, whether it has been taken/opened, a block reason (null = takeable), and the pending expiry.</summary>
    SignalTakeState DescribeFor(SetupDto setup, DateTimeOffset nowUtc);

    /// <summary>The no-op default: every signal is reported in the wire default (Auto, not taken, takeable).</summary>
    public static ISignalTakeStateProvider None { get; } = new NoneProvider();

    private sealed class NoneProvider : ISignalTakeStateProvider
    {
        public SignalTakeState DescribeFor(SetupDto setup, DateTimeOffset nowUtc) => SignalTakeState.Default;
    }
}

/// <summary>The take-state a <see cref="ISignalTakeStateProvider"/> reports for one signal — projected straight onto the
/// optional <see cref="RankedSignalDto"/> tail. Strings are wire enum member names (no magic strings on the wire).</summary>
public readonly record struct SignalTakeState(
    string EntryMode, bool IsTaken, string? BlockReason, string? ExpiresAtUtc)
{
    /// <summary>The takeable-unknown default (Auto, not taken, no block, no expiry) — the wire default.</summary>
    public static SignalTakeState Default { get; } = new("Auto", IsTaken: false, BlockReason: null, ExpiresAtUtc: null);
}
