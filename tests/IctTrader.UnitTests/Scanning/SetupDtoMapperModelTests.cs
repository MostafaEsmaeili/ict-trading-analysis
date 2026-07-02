using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Application.Scanning;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Pins the multi-model deterministic-id rule (plan §16 D1): the CANONICAL Ict2022 id formula is FROZEN
/// byte-identical (every persisted paper trade / replay-idempotency key predates the model dimension), while a
/// non-default model appends its name to the hash so two models confirming the same bar can never collide.
/// </summary>
public sealed class SetupDtoMapperModelTests
{
    private static readonly DateTimeOffset DetectedAt = new(2024, 7, 1, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Ict2022_id_is_the_frozen_legacy_formula()
    {
        var dto = SetupDtoMapper.ToDto(NewSetup(SetupModel.Ict2022), Killzone.LondonOpen, DetectedAt);

        dto.Id.Should().Be(LegacyId("EURUSD|Intraday|M5|Bullish|1.0850|1.0800|2024-07-01T06:30:00.0000000+00:00"),
            "the pre-multi-model id formula must never change for the canonical model — persisted trades and " +
            "replay idempotency key on it");
        dto.Model.Should().Be("Ict2022");
    }

    [Fact]
    public void A_non_default_model_appends_its_name_to_the_id_hash()
    {
        var ict2022 = SetupDtoMapper.ToDto(NewSetup(SetupModel.Ict2022), Killzone.LondonOpen, DetectedAt);
        var ict2024 = SetupDtoMapper.ToDto(NewSetup(SetupModel.Ict2024), Killzone.LondonOpen, DetectedAt);

        ict2024.Id.Should().NotBe(ict2022.Id, "two models confirming the same bar must never collide into one id");
        ict2024.Id.Should().Be(LegacyId(
            "EURUSD|Intraday|M5|Bullish|1.0850|1.0800|2024-07-01T06:30:00.0000000+00:00|Ict2024"));
        ict2024.Model.Should().Be("Ict2024");
    }

    private static Setup NewSetup(SetupModel model)
    {
        var ladder = new TargetLadder(
            Direction.Bullish,
            [new Price(1.0900m), new Price(1.0950m)],
            TargetLadder.CanonicalRunnerIndex);
        var plan = new TradePlan(Direction.Bullish, new Price(1.0850m), new Price(1.0800m), ladder);
        return new Setup(
            new Symbol("EURUSD"), TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 65, plan,
            new SetupReason("test"), DetectedAt, stackedFartherBound: null, model: model);
    }

    // The FROZEN legacy hash formula, restated verbatim: SHA-256 of the invariant-culture natural-identity key,
    // first 16 bytes as the GUID. If the production formula drifts, these tests fail.
    private static Guid LegacyId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString(CultureInfo.InvariantCulture)));
        return new Guid(hash.AsSpan(0, 16));
    }
}
