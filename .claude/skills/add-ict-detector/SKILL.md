---
name: add-ict-detector
description: Step-by-step procedure to add a new PURE ICT detector to IctTrader.Domain — write the failing unit test first, implement the deterministic detector, register it in the scan pipeline, add its confluence weight, and surface every constant in appsettings. Use when extending the detector set (e.g. BreakerBlock, MitigationBlock, SMT divergence, Silver Bullet, Turtle Soup).
allowed-tools: Read Write Edit Grep Glob Bash(dotnet test *)
---
# Add an ICT detector (TDD, domain-pure / DDD)
The detector and ALL its decision logic live in the DOMAIN (`IctTrader.Domain`). Detectors are pure
domain services over value objects — no I/O, no `DateTime.Now` (inject the BCL `TimeProvider`), no infra
refs, no anemic leakage.

1. **Spec** — get the exact rule from the `ict-methodology` skill / plan §2.5. Write it as IF/THEN with
   exact numbers, in the ubiquitous language (sweep, displacement, FVG, OTE…). If ambiguous, consult
   the `ict-domain-expert` agent.
2. **RED** — in `tests/IctTrader.UnitTests/Detection/<Name>DetectorTests.cs`, build a hand-crafted
   candle fixture encoding the positive case + at least one boundary + one negative + one INVALIDATION
   case (e.g. FVG two-touch, ITH/ITL breach). Run `dotnet test tests/IctTrader.UnitTests`; confirm it
   fails for the right reason.
3. **GREEN** — implement `<Name>Detector : ISetupDetector` in `src/IctTrader.Domain/Detection/Detectors/`.
   PURE. Operate on domain value objects (`Candle`, `Price`, `Pips`, `FairValueGap`…); reuse
   `MarketContext` windows and existing primitives — never duplicate swing/FVG logic. Return a
   `DetectorResult` carrying Direction, KeyLevel, ReasonFragment, Evidence, and an Invalidation flag.
4. **Constants** — every threshold comes from an Options POCO bound to `Ict:*` in appsettings. No literals.
5. **Register** — add the detector to the pipeline registration and give it a `ConfluenceCondition`.
6. **Weight + grade** — add its weight to `ConfluenceOptions` (§2.5.3); update RequiredConditions if mandatory.
7. **REFACTOR + verify** — re-run the unit suite; paste the passing output. Detector stays deterministic.
