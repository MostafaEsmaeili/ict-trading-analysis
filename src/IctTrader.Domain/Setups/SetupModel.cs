namespace IctTrader.Domain.Setups;

/// <summary>
/// The named setup/strategy model a scan cell runs — the discriminator for the multi-model architecture
/// (plan §16). A model is a complete mined methodology: a detector-pipeline recipe plus its confluence
/// weight/required-condition preset, resolved by the Scanning module's model catalog. Values are wire-stable
/// enum member names (the Direction/Killzone/Style precedent) — append-only, never renumber/rename.
/// </summary>
public enum SetupModel
{
    /// <summary>The ICT 2022 Mentorship intraday FVG model (plan §2.5) — the original, canonical model:
    /// liquidity sweep → MSS/displacement → PD-array OTE entry → draw-on-liquidity targets.</summary>
    Ict2022 = 0,

    /// <summary>The ICT 2024 Mentorship model (plan §2.6), mined from the 25-episode 2024 lecture series.</summary>
    Ict2024 = 1,
}
