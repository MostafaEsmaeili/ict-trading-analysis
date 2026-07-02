using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Scanning.Contracts;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// Pure mapping from the domain <see cref="Setup"/> aggregate to the wire <see cref="SetupDto"/>
/// (Scanning.Contracts). The enum-like wire fields carry the domain enum MEMBER NAMES verbatim (via
/// <see cref="Enum.ToString()"/>) so the dashboard/Gherkin contract stays language-neutral and no literal is
/// hand-typed here.
///
/// <para><b>Canonical target ordering on the wire</b> (Architecture A): <see cref="SetupDto.Targets"/>[0] = the
/// T1 partial, [1] = the runner (the reward-to-risk gated-draw tier), [2..] = deeper advisory SD targets. This
/// is exactly the order of <see cref="TargetLadder.Targets"/> — T1 at index 0, the runner at
/// <see cref="TargetLadder.RunnerIndex"/> (1), then the strictly-beyond SD extensions — so the wire list is the
/// ladder list unchanged.</para>
///
/// <para>The <see cref="SetupDto.Id"/> is DETERMINISTIC — derived from the setup's natural identity
/// (symbol, style, timeframe, direction, entry, stop, detection time) — because the advisory <see cref="Setup"/>
/// carries no identity of its own. A deterministic id keeps "same candles → same wire message" total (the whole
/// DTO replays identically) and hands the downstream consumer a free idempotency key: a replayed or redelivered
/// candle yields the SAME id, so PaperTrading cannot open a duplicate trade from one setup. The
/// <see cref="SetupDto.Killzone"/> is supplied by the scanner from the confirming candle's session — the
/// <see cref="Setup"/> does not carry it. <see cref="SetupDto.IsAdvisoryOnly"/> mirrors the structural
/// <see cref="Setup.IsAdvisoryOnly"/> (always true, §6.3 guardrail).</para>
/// </summary>
internal static class SetupDtoMapper
{
    public static SetupDto ToDto(Setup setup, Killzone killzone, DateTimeOffset detectedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(setup);

        var plan = setup.Plan;
        var targets = plan.Targets.Targets.Select(price => price.Value).ToArray();

        return new SetupDto(
            Id: DeterministicId(setup, detectedAtUtc),
            Symbol: setup.Symbol.Value,
            Direction: setup.Direction.ToString(),
            Killzone: killzone.ToString(),
            Style: setup.Style.ToString(),
            Grade: setup.Grade.ToString(),
            TriggerTimeframe: setup.Timeframe.ToString(),
            Entry: plan.Entry.Value,
            Stop: plan.Stop.Value,
            Targets: targets,
            RewardRatio: plan.RewardRatio.Value,
            Reason: setup.Reason.Text,
            DetectedAtUtc: detectedAtUtc,
            IsAdvisoryOnly: setup.IsAdvisoryOnly,
            // ADDITIVE: surface the 0–100 confluence score the FSM produced (carried on the Setup aggregate via
            // SetupConfirmation.Score → SetupFactory). The Signals feed ranks on it within a grade. It is NOT a
            // DeterministicId input — the id hashes the natural identity only, so adding the score never changes the id.
            Score: setup.Score,
            Model: setup.Model.ToString());
    }

    /// <summary>
    /// A stable GUID derived (SHA-256, first 16 bytes) from the setup's natural identity, formatted
    /// culture-invariantly so the same setup always hashes to the same id on any host. Two distinct setups
    /// differ in at least one component (a symbol confirms one setup per bar-close, direction, and price).
    /// </summary>
    private static Guid DeterministicId(Setup setup, DateTimeOffset detectedAtUtc)
    {
        var plan = setup.Plan;
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{setup.Symbol.Value}|{setup.Style}|{setup.Timeframe}|{setup.Direction}|{plan.Entry.Value}|{plan.Stop.Value}|{detectedAtUtc:O}");

        // Multi-model id rule (plan §16 D1): the CANONICAL Ict2022 key is frozen byte-identical — every id ever
        // persisted (paper trades keyed by SetupId, replay idempotency) predates the model dimension and MUST
        // keep hashing to the same GUID. A NON-default model appends its name, so two models confirming the same
        // bar at the same prices can never collide into one id (which would silently drop the second trade).
        if (setup.Model != SetupModel.Ict2022)
        {
            key = string.Create(CultureInfo.InvariantCulture, $"{key}|{setup.Model}");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }
}
