using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Sessions;

/// <summary>
/// The outcome of classifying one instant against the killzone schedule. <see cref="LunchBlocked"/> means
/// the HARD no-trade lunch window overrode everything; <see cref="NoNewEntry"/> is the index-AM advisory
/// cutoff (still in the killzone, but new entries are blocked).
/// </summary>
public readonly record struct KillzoneClassification(Killzone Killzone, bool LunchBlocked, bool NoNewEntry)
{
    public static KillzoneClassification None => new(Killzone.None, LunchBlocked: false, NoNewEntry: false);
}

/// <summary>
/// Classifies a candle's UTC open time into an ICT killzone in New-York time (plan §2.5.5/§4.8), honouring
/// the instrument-class split (FX vs index futures), the HARD lunch override, and the index-AM last-entry
/// cutoff. All NY conversion goes through <see cref="NyClock"/>, so the result is identical whatever zone
/// the host runs in. Pure and deterministic — no ambient clock.
/// </summary>
public sealed class KillzoneClock
{
    private readonly NyClock _nyClock;
    private readonly KillzoneSchedule _schedule;

    public KillzoneClock(NyClock nyClock, KillzoneSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(nyClock);
        ArgumentNullException.ThrowIfNull(schedule);
        _nyClock = nyClock;
        _schedule = schedule;
    }

    /// <summary>The New-York calendar date of an instant — the financial day boundary (00:00 NY).</summary>
    public DateOnly NewYorkDate(DateTimeOffset instant) => _nyClock.NewYorkDate(instant);

    /// <summary>Classifies an instant for the given instrument class.</summary>
    public KillzoneClassification Classify(DateTimeOffset openTimeUtc, InstrumentClass instrumentClass)
    {
        var nyTime = _nyClock.NewYorkTimeOfDay(openTimeUtc);

        // Lunch is a HARD no-trade window for BOTH classes and overrides any killzone (§2.5.10 #4).
        if (_schedule.Lunch.Contains(nyTime))
        {
            return new KillzoneClassification(Killzone.None, LunchBlocked: true, NoNewEntry: false);
        }

        return instrumentClass == InstrumentClass.Index
            ? ClassifyIndex(nyTime)
            : ClassifyFx(nyTime);
    }

    /// <summary>
    /// Whether this instant is a tradeable killzone ENTRY for the given active set. The active-killzone
    /// vocabulary is instrument-class-dependent (Index draws from {Am, Asian}), so an Index AM entry is
    /// not gated on membership in the FX-flavoured set.
    /// </summary>
    public bool IsActiveEntry(
        DateTimeOffset openTimeUtc,
        InstrumentClass instrumentClass,
        IReadOnlyCollection<Killzone> activeKillzones)
    {
        ArgumentNullException.ThrowIfNull(activeKillzones);
        var classification = Classify(openTimeUtc, instrumentClass);
        return classification.Killzone != Killzone.None
            && !classification.LunchBlocked
            && !classification.NoNewEntry
            && activeKillzones.Contains(classification.Killzone);
    }

    private KillzoneClassification ClassifyFx(TimeOnly nyTime)
    {
        if (_schedule.LondonOpen.Contains(nyTime))
        {
            return new KillzoneClassification(Killzone.LondonOpen, false, false);
        }

        if (_schedule.NewYorkOpen.Contains(nyTime))
        {
            return new KillzoneClassification(Killzone.NewYorkOpen, false, false);
        }

        // 10:00 hands New-York-Open (exclusive end) to London-Close (inclusive start).
        if (_schedule.LondonClose.Contains(nyTime))
        {
            return new KillzoneClassification(Killzone.LondonClose, false, false);
        }

        if (_schedule.Pm.Contains(nyTime))
        {
            return new KillzoneClassification(Killzone.Pm, false, false);
        }

        return _schedule.Asian.Contains(nyTime)
            ? new KillzoneClassification(Killzone.Asian, false, false)
            : KillzoneClassification.None;
    }

    private KillzoneClassification ClassifyIndex(TimeOnly nyTime)
    {
        if (_schedule.IndexAm.Contains(nyTime))
        {
            var noNewEntry = nyTime >= _schedule.IndexAmLastEntry;
            return new KillzoneClassification(Killzone.Am, false, noNewEntry);
        }

        return _schedule.Asian.Contains(nyTime)
            ? new KillzoneClassification(Killzone.Asian, false, false)
            : KillzoneClassification.None;
    }
}
