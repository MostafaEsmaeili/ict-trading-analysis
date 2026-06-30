namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>Why a <see cref="IctTrader.PaperTrading.Contracts.TakeSetupCommand"/> could not open a trade — the Host maps
/// each reason to an HTTP status (NotFound/Expired → 404; AlreadyTaken → 409). No magic strings.</summary>
public enum TakeSetupFailure
{
    /// <summary>No pending opportunity exists for the id (never confirmed in Manual mode, or already consumed/cleared).</summary>
    NotFound,

    /// <summary>The pending existed but aged out / its killzone window closed before the operator took it.</summary>
    Expired,

    /// <summary>A trade (or armed entry) already exists under this setup's deterministic id — it was already taken/opened.</summary>
    AlreadyTaken,
}

/// <summary>
/// Thrown by <see cref="TakeSetupCommandHandler"/> when a take cannot proceed (the pending is gone/expired or the setup
/// was already taken). The Host catches it and maps <see cref="Reason"/> to 404/409 — so the bus command itself returns
/// no result yet conveys the precise outcome. It is NOT an order/broker error (none exists, §6.3); it is purely a
/// pending-board lookup outcome.
/// </summary>
public sealed class TakeSetupException : Exception
{
    public TakeSetupException(TakeSetupFailure reason)
        : base($"Cannot take setup: {reason}.")
    {
        Reason = reason;
    }

    public TakeSetupFailure Reason { get; }
}
