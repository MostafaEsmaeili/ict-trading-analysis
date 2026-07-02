using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Setups;

/// <summary>
/// The per-symbol scan driver (plan §3.0/§4.1): for each incoming candle it updates the
/// <see cref="MarketContext"/>, runs the detector pipeline in its PINNED canonical order, feeds the scoring
/// matches into the <see cref="SetupCandidate"/> FSM, and returns the graded <see cref="SetupConfirmation"/>
/// when one confirms. A pure domain process — the Scanning module's bus handler wraps it (CandleIngested →
/// OnCandle → publish SetupConfirmed); the domain decides, the handler orchestrates.
///
/// <para><b>Pinned order matters:</b> the detectors are run in the order supplied (the canonical structural
/// chain swing → pool → sweep → displacement → MSS → FVG/OB → bias/PD/OTE → calendar). Combined with the
/// same-candle breach recognition in the MSS detector, this makes the breach-vs-MSS race deterministic
/// (spec §5 item 19) — a legitimate shift is never dropped by detector ordering.</para>
/// </summary>
public sealed class ScanSession
{
    private readonly MarketContext _context;
    private readonly IReadOnlyList<ISetupDetector> _detectors;
    private readonly ISetupCandidate _candidate;
    private readonly SetupCandidateOptions _options;

    private DateOnly? _lastNyDate;
    private Killzone _lastKillzone = Killzone.None;

    public ScanSession(
        MarketContext context,
        IReadOnlyList<ISetupDetector> detectors,
        ISetupCandidate candidate,
        SetupCandidateOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(detectors);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(options);
        _context = context;
        _detectors = detectors;
        _candidate = candidate;
        _options = options;
    }

    public MarketContext Context => _context;

    public ISetupCandidate Candidate => _candidate;

    /// <summary>Processes one candle and returns a confirmed setup when one grades at or above the alert floor.</summary>
    public SetupConfirmation? OnCandle(Candle candle)
    {
        _context.Append(candle);

        ResetCandidateOnBoundary();

        var matches = new List<ConfluenceMatch>();
        foreach (var detector in _detectors)
        {
            var result = detector.Detect(_context, candle);
            if (result.Matched && detector.Condition is { } condition)
            {
                matches.Add(new ConfluenceMatch(condition, result));
            }
        }

        return _candidate.Observe(_context, candle, matches);
    }

    // The §2.5 setup must assemble within one financial day and (optionally) one killzone — reset the in-flight
    // candidate when the candle crosses either boundary so stale structure cannot leak across.
    private void ResetCandidateOnBoundary()
    {
        var nyDate = _context.CurrentNewYorkDate;
        if (_lastNyDate is not null && nyDate != _lastNyDate)
        {
            _candidate.Reset();
        }

        _lastNyDate = nyDate;

        var killzone = _context.Session.Killzone;
        if (_options.ResetOnKillzoneChange && killzone != _lastKillzone)
        {
            _candidate.Reset();
        }

        _lastKillzone = killzone;
    }
}
