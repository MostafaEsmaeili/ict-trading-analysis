using System.Globalization;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Host;

/// <summary>The market-session status for the dashboard (plan §2.1/§4.8) — whether the FX market is open now, the
/// current ICT killzone/session, and the next active killzone open. All NY-time (DST-aware via NyClock). Read-only.</summary>
public sealed record MarketStatusDto(
    string NowUtc,
    string NowNy,
    string DayOfWeekNy,
    bool MarketOpen,
    string CurrentSession,
    bool InActiveKillzone,
    string? NextSession,
    int? NextSessionOpensInMinutes,
    string? NextSessionStartsNy,
    IReadOnlyList<string> ActiveKillzones);

/// <summary>Computes the <see cref="MarketStatusDto"/> from the DST-aware NY clock + the killzone schedule. Pure
/// read-only session math — no order/broker surface (§6.3 guardrail).</summary>
internal static class MarketStatus
{
    // Spot FX trades ~24/5: it opens Sunday 17:00 ET and closes Friday 17:00 ET (the rollover/maintenance cutoff).
    private static readonly TimeOnly FxWeekBoundaryNy = new(17, 0);
    private const int LookaheadMinutes = 8 * 24 * 60; // scan up to ~8 days for the next active killzone open

    public static MarketStatusDto Compute(TimeProvider timeProvider, IReadOnlyList<Killzone> activeKillzones)
    {
        var nyClock = new NyClock(timeProvider);
        var killzoneClock = new KillzoneClock(nyClock, KillzoneSchedule.CreateDefault());
        var nowUtc = nyClock.UtcNow;
        var nowNy = nyClock.ToNewYork(nowUtc);

        var marketOpen = IsFxOpen(nowNy);
        var active = activeKillzones.ToHashSet();
        var current = killzoneClock.Classify(nowUtc, InstrumentClass.Fx).Killzone;

        // When the market is closed (weekend) the time-of-day killzone is moot — those windows don't trade until the
        // Sunday 17:00 ET reopen — so report "Closed" and find the next active killzone that lands WHILE the market is
        // open (e.g. from a Sunday morning the next session is Monday's London Open, not "NewYorkOpen at Sun 07:00").
        var currentSession = marketOpen ? current.ToString() : "Closed";
        var inActive = marketOpen && active.Contains(current);

        var (nextKz, nextStartUtc) = FindNextActiveOpen(nyClock, killzoneClock, nowUtc, active);

        return new MarketStatusDto(
            NowUtc: nowUtc.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture),
            NowNy: nowNy.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DayOfWeekNy: nowNy.DayOfWeek.ToString(),
            MarketOpen: marketOpen,
            CurrentSession: currentSession,
            InActiveKillzone: inActive,
            NextSession: nextKz?.ToString(),
            NextSessionOpensInMinutes: nextStartUtc is { } s ? (int)Math.Round((s - nowUtc).TotalMinutes) : null,
            NextSessionStartsNy: nextStartUtc is { } st
                ? nyClock.ToNewYork(st).ToString("ddd HH:mm", CultureInfo.InvariantCulture)
                : null,
            ActiveKillzones: activeKillzones.Select(k => k.ToString()).ToArray());
    }

    /// <summary>FX is closed Fri ≥17:00 NY through Sun &lt;17:00 NY (and all of Saturday).</summary>
    private static bool IsFxOpen(DateTimeOffset ny)
    {
        var t = TimeOnly.FromTimeSpan(ny.TimeOfDay);
        return ny.DayOfWeek switch
        {
            DayOfWeek.Saturday => false,
            DayOfWeek.Friday => t < FxWeekBoundaryNy,
            DayOfWeek.Sunday => t >= FxWeekBoundaryNy,
            _ => true,
        };
    }

    /// <summary>Steps forward minute-by-minute to the next instant an ACTIVE killzone window opens (a transition into
    /// it), so the UI can show "NewYorkOpen opens in 42m". Returns null if none is found within the lookahead.</summary>
    private static (Killzone? Killzone, DateTimeOffset? StartUtc) FindNextActiveOpen(
        NyClock nyClock, KillzoneClock clock, DateTimeOffset nowUtc, IReadOnlySet<Killzone> active)
    {
        var prev = clock.Classify(nowUtc, InstrumentClass.Fx).Killzone;
        for (var m = 1; m <= LookaheadMinutes; m++)
        {
            var at = nowUtc.AddMinutes(m);
            var kz = clock.Classify(at, InstrumentClass.Fx).Killzone;
            // A transition INTO an active killzone — but only count it if the market is actually open then (skips the
            // weekend-closed window so the "next session" is the next TRADEABLE one).
            if (kz != prev && active.Contains(kz) && IsFxOpen(nyClock.ToNewYork(at)))
            {
                return (kz, at);
            }

            prev = kz;
        }

        return (null, null);
    }
}
