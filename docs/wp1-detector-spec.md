<!--
  WP1 detection-layer implementation spec. Produced by the `wp1-detector-spec` workflow
  (wf_7ebc790e-842): 7 parallel agents derived each detector group's C# spec from plan §2.5, each was
  adversarially ICT-fidelity-checked, and this is the consolidated result. It is the implementation
  reference for issue #3 — defaults here are configurable and the §5 "unresolved ICT issues" need a
  human/transcript sign-off. Plan §2.5/§2.5.10 remains the source of truth on any conflict.
-->

# WP1 — IctTrader.Domain Detection Layer: Consolidated Implementation Plan

Scope: the **pure, deterministic** detection layer in `src/IctTrader.Domain` (no I/O, no `DateTime.Now`, no module references). Folds in **every** verifier `correctedNotes`. Existing scaffolding to **reuse, not recreate**: `ValueObjects/{Candle, Direction, Timeframe, Symbol, Price, PriceRange, Risk(RewardRatio/RiskPercent), OteZone, Tick}.cs`, `Sessions/{NyClock, Killzone}.cs`, `Styles/TradeStyle.cs (+ TimeframePolicy)`, `Setups/SetupGrade.cs`, `Common/{Entity, Guard}.cs`.

**Frozen-contract deltas to declare explicitly (WP0 enums, PLAN §11.1 / line ~1086):**
- `Timeframe` enum uses `D1/W1/MN1` — specs say "Daily/Weekly/Monthly"; **map Daily→D1, Weekly→W1, Monthly→MN1** everywhere; do NOT add new members.
- `Direction` is `{Bullish, Bearish}` only (no `Neutral`). Bias "Neutral" = `Direction?` **null**, not an enum member.
- `Killzone` frozen set is `{None, Asian, LondonOpen, NewYorkOpen, LondonClose}`. **`PM` (FX) and `AM` (Index) are a WP1 extension** — add them and annotate the enum with a `// WP1 delta (PLAN §11.1 permits): PM, AM` note so the contract delta is explicit (per clock verdict fix 5).

---

## (1) Build order (dependency-ordered)

Strictly bottom-up; each tier compiles and unit-tests green before the next.

**Tier 0 — Value objects / enums (mostly exist; add the missing ones)**
1. Reuse: `Candle, Direction, Timeframe, Symbol, Price, PriceRange, RewardRatio, RiskPercent, OteZone, Tick`.
2. Add `SymbolSpec` VO (or extend `Symbol`): `PipSize`, `TickSize`, `InstrumentClass {FX, Index}`, `Digits`. Drives every pip↔price conversion (no magic numbers).
3. Add `InstrumentClass` enum `{FX, Index}`.
4. Add `ConfluenceCondition` enum (closed set, §2.5.3 — see §4 below).
5. Add `DetectorResult` `readonly record struct` (§4.2 shape).
6. Add domain VOs the detectors register: `SwingPoint`, `FairValueGap`, `OrderBlock`, `LiquidityPool`, `Displacement`, `MarketStructureShift`, `DealingRange`, each a small aggregate/entity with `Mitigate()/Void()/MarkConsumed()/Invert()` behavior (rich, not anemic).

**Tier 1 — Abstractions + per-symbol state**
7. `ISetupDetector` (pure read API; §4.2). `IReadOnlyDictionary<string,object>? Evidence`.
8. `MarketContext` (per-symbol mutable state container; ring buffers + registries + `Session` + `Bias`). `Append(Candle)` is the **only** place wall-clock state is computed, via injected `NyClock`.
9. Evidence-key constants class (`EvidenceKeys`) + `.resx` reason templates (`SetupReasons.resx`, `ValidationMessages.resx`) — no magic strings.

**Tier 2 — Time services (no detector depends on these compiling-wise except via ctx)**
10. `NyClock` — **fix `IsDst` derivation** (verdict fix 1) + identity validator (fix 4). Already scaffolded; amend.
11. `KillzoneClock` (the first real `ISetupDetector`, emits `KillzoneEntry`).

**Tier 3 — Structural / PD-array detectors (mutate registries)**
12. `SwingPointDetector` (feeder; no condition).
13. `LiquidityPoolDetector` (feeder) → `LiquiditySweepDetector` (`LiquiditySweep` 0.95) → `DrawOnLiquidityDetector` (`DrawTargetRrMet` 0.65).
14. `DisplacementDetector` (precondition, **non-scoring**) → `MarketStructureShiftDetector` (emits the single `DisplacementMss` 0.95).
15. `FairValueGapDetector` (`FvgPresent` 0.9) — depends on swing + displacement + sweep registries.
16. `OrderBlockDetector` (`OrderBlockConfluence` 0.65) — gated on `OpenFvgs`.

**Tier 4 — Bias / PD geometry**
17. `DealingRangeEquilibriumDetector` (**informational, non-scoring** — verdict fix 1).
18. `DailyBiasDetector` (`BiasAligned` 0.85).
19. `PremiumDiscountGateDetector` (**sole owner** of `PremiumDiscountHalf` 0.85; hard veto).
20. `OteFibDetector` (`OteZone` 0.7) — depends on displacement leg + FVG/OB + PD gate.

**Tier 5 — Calendar gate (new, required by foundation verdict fix 1)**
21. `CalendarGateDetector` (emits the **required** `CalendarClear` condition) — hard no-trade on post-FOMC / NFP Thu+Fri / NFP-week Wed+. Consumes `MarketContext.CalendarDriversForNyDate` (an explicit dependency injected at scan time, sourced later from EconomicCalendarFilter — not assumed present).

**Tier 6 — Trade style (config-driven, no candle math)**
22. `TimeframePolicyResolver` → `TradeStylePolicyValidator` (`ValidateOnStart`) → `DetectedSetupStyleClassifier`.

**Cross-cutting last:** Roslyn/arch test asserting zero `DateTime.Now / DateTimeOffset.Now / DateTime.UtcNow / TimeProvider.LocalTimeZone / TimeZoneInfo.Local` in `IctTrader.Domain`.

---

## (2) Per-detector implementation checklists (correctedNotes folded in)

### Tier 1 abstractions

**`ISetupDetector` / `DetectorResult` / `ConfluenceCondition`**
- [ ] `DetectorResult` invariants: `NoMatch => new(false,null,null,"",null)`; `Matched=false ⇒ all null + ReasonFragment==""`; `Matched=true ⇒ ReasonFragment` non-empty (from `.resx`, numbers interpolated). Structural (record-struct) equality.
- [ ] `Detect` is **total** — return `NoMatch` (never throw) on small window / no pattern.
- [ ] Evidence keys are `const` (`EvidenceKeys.*`); values primitive/decimal/string/bool/enum-name; serialize JSONB with **sorted keys**.
- [ ] Invalidation channel: `Evidence[EvidenceKeys.Invalidated]=true` (+ `VoidedArrayId`) distinguishes void from formation; FSM treats it as teardown, not a confluence add.
- [ ] Arch test: confluence-only detectors mutate **no** registry; structural detectors mutate only their declared registry.

**`MarketContext`**
- [ ] `Append(Candle)` pushes to `Window(tf)` ring buffer (cap `WindowCapacity`=512), then recomputes `Session` via `NyClock`, then refreshes `Bias` inputs. Only place wall-clock state is set.
- [ ] `Window(tf)` newest-at-`[^1]` (`w[^3]=c1, w[^2]=c2, w[^1]=c3` per §4.3).
- [ ] Registries hold **open arrays only**; evict on `Mitigate/Void/Consumed` and cap at `MaxOpenArraysPerType`=64.
- [ ] Roll session-scoped state (`AsianRange`, intraday pools) at 00:00 NY when `ResetSessionStateAtNyMidnight`=true.
- [ ] Determinism test: identical candle list + same `TimeProvider` ⇒ field-equal contexts (replay == live).

### Tier 2 time

**`NyClock` (amend existing)**
- [ ] **Fix IsDst (clock verdict issue 1 / fix 1):** derive from the UTC instant's offset, never `IsDaylightSavingTime(localDateTime)`. `UtcOffset(utc) = _newYork.GetUtcOffset(utc)`; `IsDst(utc) = !UtcOffset(utc).Equals(_newYork.BaseUtcOffset)`. This makes the fall-back overlap correct: `05:30Z ⇒ 01:30 NY, -240, IsDst=true`; `06:30Z ⇒ 01:30 NY, -300, IsDst=false`. `UtcOffsetMinutes` derived from the same single source.
- [ ] **Identity validator (fix 4):** startup validator asserts the resolved zone corresponds to IANA `America/New_York`; fail fast if `FindSystemTimeZoneById` throws **or** the configured id is the Windows `"Eastern Standard Time"`. Keep `BaseUtcOffset == -05:00` only as a **secondary sanity assert**, not the primary lock.
- [ ] All conversions pure; `NowNy()` reads `_timeProvider.GetUtcNow()` only.
- [ ] Tests: summer EDT −240 / winter EST −300; spring-forward 06:59Z→01:59 NY, 07:00Z→03:00 NY (gap skipped, monotonic); fall-back overlap per fixed IsDst; unresolvable zone + Windows-id both fail fast; `NowNy` independent of `Asia/Tokyo` process zone.

**`KillzoneClock`** — condition `KillzoneEntry` (1.0, **required**)
- [ ] Windows NY-local, **inclusive start / exclusive end**; Asian wrap `[19:00,24:00)`. FX: LondonOpen `[02:00,05:00)`, NewYorkOpen `[07:00,10:00)`, LondonClose `[10:00,11:00)`, PM `[13:30,16:00)`, Asian `[19:00,00:00)`. Index: AM `[08:30,11:00)`, Asian.
- [ ] **HARD lunch `[12:00,13:00)`** both classes ⇒ `Killzone=None, LunchBlocked=true`, `KillzoneEntry` fails, overrides everything.
- [ ] **Index active-set fix (clock verdict issue 2 / fix 2):** gate Index entries on `InstrumentClass==Index` + Index killzone vocabulary `{AM, Asian}` (the allowed `ActiveKillzones` set is **instrument-class-dependent**) — do NOT require Index `AM` to be a member of the FX-flavoured set, or AM can never match. Document the chosen contract in evidence.
- [ ] **News extension (clock verdict issue 3 / fix 3):** keep `NewsExtensionEnabled` flag. Default-off is a **documented, deliberate** safety deviation pending EconomicCalendarFilter; pin that the **active morning window end** extends to 11:30; cite §2.5.1 step3 in the `.resx` reason; treat `NewsFlagsForNyDate` as an explicit dependency on the calendar detector, not an assumed field.
- [ ] Index AM `NoNewEntry=true` when `t >= 10:40` (advisory; still in killzone, blocks **new** entry only).
- [ ] `Matched(KillzoneEntry) = Killzone!=None && in active set && !LunchBlocked && (Index ⇒ !NoNewEntry)`; `Direction=null`, `KeyLevel=null`.
- [ ] **Enum-delta note (fix 5):** PM/AM extend the WP0-frozen `Killzone` set — annotate as a WP1 contract delta.
- [ ] Resolve config conflicts with comment cites: London Close `10:00–11:00` (§2.5.10 #3 over §2.1's 10:00–12:00); FX NY `07:00–10:00` (§2.5.10 #6 over §2.1's 07:00–09:00); lunch `12:00–13:00` (#4); Asian `19:00–00:00` (#6). Provenance: do **not** treat NY-Open as the Silver-Bullet window.
- [ ] Tests: mid-window summer match host-zone-independent; inclusive-start/exclusive-end (10:00 hands NewYorkOpen→LondonClose); lunch override; dead gap 13:00–13:30; dead time 06:30; Index 10:50 `NoNewEntry`; Asian wrap 23:30 vs 00:30; DST spring/fall identical across forced zones.

### Tier 3 structural

**`SwingPointDetector`** (feeder, no condition)
- [ ] 3-candle fractal (`SwingFractalWidth`=3, configurable; `StrictInequality`=true so equal H/L are **liquidity, not swings**). Pivot `w[^2]` vs `w[^3]/w[^1]`.
- [ ] **Direction convention (structure verdict fix 4 / liquidity issue):** **pin in the contract** — `SwingHigh` = buy-side liquidity, enables a **bearish** trade (`Direction.Bearish`); `SwingLow` = sell-side, enables **bullish**. Document on the type, not as an open issue, so Sweep/MSS/StopPlacement agree.
- [ ] Invalidation = ITH/ITL **close-beyond** (`InvalidateOnCloseBeyond`=true; close > swingHigh / close < swingLow). Wick-only beyond ⇒ `WickBeyondNoClose=true` (a sweep, not a breach). Cite SKILL line 17 ("3-candle definition") in the rule comment to forestall a 5-candle misread.
- [ ] `MarkConsumed` when sweep/MSS references the swing; evict past cap.

**`LiquidityPoolDetector`** (feeder)
- [ ] BSL above old/equal highs, SSL below old/equal lows; relative-equal cluster within `EqualLevelTolerancePips` (**default 1.5; flag as invented, non-transcript, needs WP1/per-symbol/ATR calibration** — foundation verdict fix 2 + liquidity openIssue). Inclusive `<=` at tolerance boundary.
- [ ] Register prior-day H/L, prior-session H/L, big figures `[0,20,50,80]`, Asian H/L. Merge clusters (Level=mean, Strength=count). De-dup within tolerance (extend Strength).
- [ ] Pool `Untapped` flips false on first wick-through (mitigation). Consumption distinguishes **Sweep** (wick beyond + close back inside) vs **Run** (close beyond ⇒ HRLR, do-not-fade, §2.5.8) via `consumption` evidence.

**`LiquiditySweepDetector`** — `LiquiditySweep` (0.95, **required**)
- [ ] **Remove the `Close vs ReferenceOpen` conjunct from MATCH (liquidity verdict fix):** MATCH on exactly two transcript-faithful gates — (a) wick penetration `High > pool.Level + SweepMinPenetrationPips*PipSize` (up-raid; mirror down), strict `>`; (b) close back inside the **pool** (`Close < pool.Level` up / `Close > pool.Level` down) as the sweep-vs-run discriminator.
- [ ] Apply premium/discount to the **penetration leg, not the close**: up-raid (short) is Judas iff the swept high traded in premium (`High > ReferenceOpen`); down-raid (long) iff `Low < ReferenceOpen`. `IsJudas` set from the raid crossing the reference open, independent of close-vs-open.
- [ ] `ReferenceOpen` = 00:00 NY default; 08:30 NY when `InstrumentClass==Index` or `UseMacroOpenReference`; when bearish & both available use the **lower** open (§2.5.8).
- [ ] Run rejection: close beyond ⇒ `RunNotSweep` (HRLR, do-not-fade), `Matched=false`.
- [ ] Decay: clear `LastSweep` + `SweepExpired` if no MSS within `SweepToMssMaxBars` (sweep-must-precede-MSS, §2.5.10).
- [ ] Comparator convention documented: penetration strict-`>` (exact-min wick is NOT a sweep); equal-level tolerance inclusive `<=`.

**`DrawOnLiquidityDetector`** — `DrawTargetRrMet` (0.65, **required**)
- [ ] DOL on the **opposite** side of entry. Candidates: opposing relative-equal pools, prior-day H/L, prior swing H/L, nearest untapped HTF FVG far boundary, big figures. **IRL before ERL** (§2.5.10) — IRL (FVG/50%) is T1, ERL is the runner.
- [ ] Select nearest DOL with `RR = |DOL−Entry|/|Entry−Stop| >= MinRewardRatio` (**2.5 default, configurable; never hard 3R**). `>=` inclusive at 2.5.
- [ ] Invalidations: `NoQualifyingDraw` (required step 2 fails), `DrawConsumed` (re-select / else invalidate), `DrawOnWrongSide`, `RRBelowFloor` (prefer farther only within `MaxDrawDistancePips`).
- [ ] Deterministic tie-break: nearest → higher Strength → ERL over IRL → stable index. Flag as an engineering decision.

**`DisplacementDetector`** (**precondition, non-scoring — no SetupScorer weight**, displacement verdict fix 1)
- [ ] Energy gate ALL of: `bodyToRange >= MinBodyToRangeRatio` (0.50) AND `body >= AtrMultiple*ATR(AtrPeriod)` (1.5×14) AND a true close-beyond. **`MinDisplacementPips` (5.0) demoted (displacement verdict issue 2 / fix 2/3):** default **OFF (0)** or disclosed-non-transcript; must NOT be a silent third AND-clause (§2.5.5 "no fixed pip"); add to openIssues; operator-tunable.
- [ ] Register `Displacement` VO; `LastDisplacement` set. Invalidate on full leg retrace (`displacement-leg-retraced`). ATR warmup ⇒ `NoMatch` (`atr-warmup`), not a weak match. Weak/wick-only ⇒ `weak-expansion` NoMatch.
- [ ] `StructureTimeframe` sourced from the active `TimeframePolicy.StructureTf` (§4.7), not hard-coded.

**`MarketStructureShiftDetector`** — emits the **single** `DisplacementMss` (0.95, **required**) (displacement verdict fix 1)
- [ ] Gate on `DisplacementDetector` verdict for `current` (consumes it; the precondition is **counted once** here — no double-0.95).
- [ ] Require precedent sweep within `SweepToMssMaxBars` (default 5). MATCH: displacement-direction-aligned + **close** strictly beyond the broken 3-candle swing by `CloseBeyondMinPips` (1.0 FX / 1 tick) + close in the trade direction.
- [ ] Invalidations: `mss-itl-breach`/`mss-ith-breach` (close back beyond origin swing; `InvalidationFractalWidth`=5 ITH/ITL — **flag 5 as UNCONFIRMED**, confirm vs Ep12), `no-precedent-sweep`, `weak-break-no-displacement`, `wick-only-break-no-close`.
- [ ] Set `StructureDirection` lock + `LastMssLevel`. Cite SKILL line 17 for the swing fractal.

**`FairValueGapDetector`** — `FvgPresent` (0.9, **required**)
- [ ] `c1=w[^3], c3=w[^1]`; bullish `c1.High<c3.Low`, bearish `c1.Low>c3.High`. `gapPips=gapSize/PipSize`.
- [ ] **Correct-half operators (structure verdict fix 1 — load-bearing bug):** LONG/bullish FVG must be in **discount** ⇒ `gap.high <= EquilibriumPrice`; SHORT/bearish FVG in **premium** ⇒ `gap.low >= EquilibriumPrice`. Set `InCorrectHalf` accordingly; never-buy-in-premium / never-sell-in-discount reject reads off the same. (Selection-rule operators were inverted in the spec.)
- [ ] Displacement-quality: `gapPips >= MinGapPips` (1.0) AND middle-candle body `>= AtrMultiple*ATR` (1.5×). Below ⇒ `LowQuality=true`. Flag `MinGapPips`/`AtrMultiple` as §2.5.7-caveat-5 placeholders.
- [ ] Top-down first-FVG: **drive `TopDownTimeframes` from the active style's `TimeframePolicy`** (structure verdict fix 3), not the fixed `[M15,M5,M3,M1]` (keep that only as the Intraday default; Swing walks H4→H1→M15). Mark exactly one `IsSelectedEntryFvg` per leg.
- [ ] Stacked flag within `StackProximityPips` (**default 5; flag as invented/non-transcript** — foundation verdict fix 2) ⇒ `Stacked=true` + `FartherFvgBoundary` (agree the field name with StopPlacement).
- [ ] Two-touch invalidation: `VoidOnTouchCount`=3 (3rd return voids, Ep38) ⇒ `FvgVoided_TwoTouch`, withdraw `FvgPresent`. **Pin touch semantics (wick-into-gap vs close-into-gap) with an Ep38-referenced unit test** before settling (foundation/structure nice-to-have).
- [ ] Mitigation: full fill ⇒ `FvgMitigated`, array dies (§2.5.10). (Open: 50%/consequent-encroachment variant — add `MitigateAtPercent` only if Eps confirm.)
- [ ] **Validity exclusions — add the missing fifth (structure verdict fix 2):** `NoSweep | InAsianRange | CounterBias | NoChoch | OverlappingWicks`. Add `RejectNoChoch` (default true) consuming `LastMss`/CHoCH-after-formation. **Enforcement mode is an open issue** — surface as `ApplyValidityExclusions` (flag-only vs hard-reject); default flag-only so the FSM still sees the array.

**`OrderBlockDetector`** — `OrderBlockConfluence` (0.65, **not required**)
- [ ] Last opposite-close candle before displacement; `KeyLevel = ob.Open`; `MeanThreshold=(Open+Close)/2`; entry = open ± `EntryOffsetPipsFx` (3) / `EntryOffsetTicks` (1).
- [ ] **HARD require linked FVG** (`RequireFvg`=true) in the same leg/direction ⇒ else `ObRejected_NoFvg`. `MaxClusterCandles`=3 (open: single last opposite-close vs whole cluster for mean-threshold — confirm Eps 06/25/26/33).
- [ ] Premium/discount gate **mirrors the corrected FVG operators**: bullish OB in discount (`<=50%`), bearish in premium (`>=50%`); else `ObRejected_WrongHalf`. Re-verify boundary fixtures (structure verdict fix 1).
- [ ] Invalidations: `ObMitigated` (close through beyond open, or linked FVG mitigates ⇒ dies-with-FVG), `ObInverted` on BOS (§2.5.10 PD-array inversion), mean breach.

### Tier 4 bias / PD

**`DailyBiasDetector`** — `BiasAligned` (0.85, **required**)
- [ ] Range body-to-body on the **broken** daily swing (§2.5.10); `EQ = low + range*0.50`; `posPct`. Discount(`<50`)⇒Bullish, Premium(`>50`)⇒Bearish, `==50`⇒**Neutral (Direction null)**, zero-range⇒Neutral.
- [ ] **3-consecutive-close confirmation default OFF (bias verdict fix 2):** flip `RequireConsecutiveCloseConfirmation` to **false** (the "3-signal AND" is §2.5.10 provenance-flagged, corroborative not a hard gate); keep the opt-in toggle.
- [ ] Invalidations: `BiasNeutralized` (posPct crosses half), `BiasFlippedOnDailyBos` (+`InvertArrays=true`), `ConsecutiveCloseBroken` (only when toggle on).
- [ ] Use shared `EquilibriumBoundaryPolicy` for the exactly-50% semantics (bias verdict fix 3).

**`DealingRangeEquilibriumDetector`** (**informational, non-scoring — bias verdict fix 1**)
- [ ] Compute `EQ`, quadrants (`0.25/0.75` band, **provenance-flagged, non-gating**), `Half {Discount,Premium,Equilibrium}`. **Remove its scored confluence** (or rename to a non-scored `DealingRangeContext`) so the 0.85 `PremiumDiscountHalf` weight is counted **once** by the gate.
- [ ] Invalidations: `RangeInvalidatedOnExpansion` (re-anchor on new broken swing), `DegenerateRange`. `AnchorMode=BodyToBody` default.

**`PremiumDiscountGateDetector`** — **sole owner** of `PremiumDiscountHalf` (0.85, **required**)
- [ ] Hard veto: short allowed iff `posPct >= 50` (premium), long iff `posPct <= 50` (discount); `InclusiveAtEquilibrium`=true (50% allowed both sides). Null direction ⇒ no gate, no trade.
- [ ] Invalidations: `GateViolation_SellInDiscount`, `GateViolation_BuyInPremium` (`veto=true` blocks confirmation regardless of other confluences), `GateClearedOnReanchor`.
- [ ] Consume the shared `EquilibriumBoundaryPolicy` (bias verdict fix 3) so exactly-50% is defined once across bias + gate.

**`OteFibDetector`** — `OteZone` (0.7, **not required**)
- [ ] Anchor **body-to-body** by default; wick only when `IsFomcOrNfp` (§2.5.10 #1) — enforce with `AnchorModeMismatch` fallback. Band `[62%,79%]` of the displacement leg (inclusive edges, flagged open), `SweetSpotFib=0.705` tagged `isPrimerSourcedDefault=true`, `EquilibriumFib=0.50`. `UseEp41Variant` toggle for 62–70% (default off).
- [ ] Require an FVG/OB key level inside the band **on the correct PD side** (consume corrected operators). Prefer level nearest sweet spot.
- [ ] Invalidations: `OteVoidedOnFvgInvalidation` (overlapping FVG two-touch void), `OteVoidedOnFullRetrace` (past 79%/leg origin — ITH/ITL breach), `OteSkippedNoOverlap`. Consumes a **pre-validated** `ctx.DisplacementLeg`; does NOT re-quantify displacement.
- [ ] No hard-coded win-rate/fill-rate. FOMC/NFP from injected calendar state, not `DateTime.Now`.

### Tier 5 calendar (NEW — foundation verdict fix 1)

**`CalendarGateDetector`** — emits `CalendarClear` (**required**, see §4 resolution)
- [ ] Hard no-trade block on post-FOMC, NFP Thursday+Friday, NFP-week Wednesday+ (the §2.5.2 ALL-must-be-true calendar clause). `Matched(CalendarClear)=true` only when the NY date is **not** calendar-blocked.
- [ ] Consume `MarketContext.CalendarDriversForNyDate` (explicit dependency, sourced from EconomicCalendarFilter later — not assumed populated). When unavailable, behavior is config-gated and documented.
- [ ] This is the deliberate resolution of the §2.5.2-vs-§2.5.3 contradiction: calendar is a **required HARD gate** (no-trade blocker), distinct from the low-weight `CalendarDriver` (0.35) score contributor, which remains weighted-optional.

### Tier 6 trade style

**`TimeframePolicyResolver`**
- [ ] Read `Ict:TradeStyles[<Style>]` Options (never literals); return immutable `TimeframePolicy` (reuse existing VO, map `Daily→D1` etc.). Missing key ⇒ throw `TradeStyleConfigurationException` (fail-fast; `ValidateOnStart` should have caught it).
- [ ] **`AllowDirectFvgEntry` for Scalp default = FALSE (style verdict fix 1):** Silver-Bullet "skip OTE retrace" is Primer-sourced/provenance-flagged; default must preserve the §2.5 OTE RequiredCondition. Keep configurable opt-in; add provenance note on the key.
- [ ] **Hold caps (style verdict fix 2/3):** Intraday cap from the "90–120 min" band. Either drop `WarnHoldMinutes` or label it explicitly as a no-provenance UX convenience (90 is an equally valid hard cap). Swing 14400 / Position 43200 ⇒ keep as **operator-tunable, flagged "NOT transcript-stated (§4.7 says days/weeks)"** on each option unit string, not just openIssues.
- [ ] `MinRewardRatio` default 2.5, floor `AbsoluteMinRewardRatio` 2.0; never raise to 3.

**`TradeStylePolicyValidator`** (`ValidateOnStart`)
- [ ] Assert strictly-descending ordinals `Bias > Structure > Entry`; `MinRewardRatio >= 2.0`; `MaxHold > 0`; `WarnHold <= MaxHold`; no-overnight ⇒ `MaxHold <= IntradayClassMaxHoldGuardMinutes` (1440); all four enum members present; `ActiveStyles` non-empty subset; `ReferenceTimeZone` resolves to IANA `America/New_York` (reject Windows id).
- [ ] Collect **all** failures (no short-circuit); messages from `ValidationMessages.resx`.

**`DetectedSetupStyleClassifier`**
- [ ] Classify by trigger-TF first, hold-band tie-break (`<=30 Scalp; <=120 Intraday; <=14400 Swing; else Position`, inclusive upper edges). `DefaultClassificationPriority=Timeframe` for TF-vs-hold conflict — flag as interpretation (style verdict fix 4b). Note M1 collision (Scalp.EntryTf == Intraday.EntryAltTf) is genuinely ambiguous.
- [ ] **Surface to product (do not silently decide, style verdict fix 4a):** whether to relabel a Setup away from the single running `ActiveStyle` (default: trust classification, fall back to `ActiveStyle` only when `Unclassified`).
- [ ] Hold computed from injected `TimeProvider`/`NyClock` UTC stamps, never `DateTime.Now`.
- [ ] Invalidations: `Unclassified`, `StyleHoldCapBreached` (runtime, past `MaxHold`), `OvernightHoldRejected` (AllowOvernight=false crossing 17:00 ET `CutoffEt`), `ConflictUnresolved` (Strict priority). `TargetDistanceTieEnabled`=false (per-style `MinTargetPips` undefined — leave for WP1).

---

## (3) Consolidated `Ict:*` Options keys (one table)

`I` = invented/non-transcript engineering default needing WP1/transcript calibration (flag in openIssues + on the unit string).

| Key | Default | Unit / notes |
|---|---|---|
| **Time** |  |  |
| `Ict:Time:ReferenceTimeZone` | `America/New_York` | IANA id; identity-validated, reject Windows id |
| `Ict:Time:DisplayTimeZone` | `America/New_York` | IANA id; display only |
| **Scanning / context** |  |  |
| `Ict:Scanning:WindowCapacity` | `512` | candles/TF ring buffer (**I**) |
| `Ict:Scanning:WarmupBars` | `200` | candles replayed to warm-start (note: §displacement spec uses 50 for ATR warmup — reconcile to one value) |
| `Ict:Scanning:MaxOpenArraysPerType` | `64` | max live FVG/OB/pool/swing per type (**I**) |
| `Ict:Scanning:ActiveStyles` | `["Intraday"]` | which styles run |
| `Ict:Scanning:ActiveKillzones` | `["LondonOpen","NewYorkOpen"]` | FX subset; Index vocabulary `{AM,Asian}` is class-dependent |
| `Ict:Scanning:ResetSessionStateAtNyMidnight` | `true` | roll Asian range/intraday pools at 00:00 NY |
| **Confluence (scorer)** |  |  |
| `Ict:Confluence:Weights:<Condition>` | per §2.5.3 (table §4) | weight 0..1 |
| `Ict:Confluence:RequiredConditions` | `[BiasAligned,KillzoneEntry,LiquiditySweep,DisplacementMss,FvgPresent,PremiumDiscountHalf,DrawTargetRrMet,CalendarClear]` | **CalendarClear added** (foundation fix 1) |
| `Ict:Confluence:GradeThresholds:A` / `:B` / `:C` | `80` / `65` / `50` | score 0..100 |
| `Ict:Confluence:AlertMinimumGrade` | `B` | alert floor 65 |
| **Killzones** |  |  |
| `Ict:Killzones:InstrumentClass` | `FX` | FX\|Index (per-symbol override) |
| `Ict:Killzones:Fx:LondonOpen` | `02:00-05:00` | NY `[start,end)` |
| `Ict:Killzones:Fx:NewYorkOpen` | `07:00-10:00` | (§2.5.10 #6) |
| `Ict:Killzones:Fx:LondonClose` | `10:00-11:00` | (§2.5.10 #3) |
| `Ict:Killzones:Fx:Pm` | `13:30-16:00` |  |
| `Ict:Killzones:Index:Am` | `08:30-11:00` |  |
| `Ict:Killzones:Index:AmLastEntry` | `10:40` | advisory NoNewEntry cutoff |
| `Ict:Killzones:Asian` | `19:00-00:00` | wrap-around |
| `Ict:Killzones:Lunch` | `12:00-13:00` | HARD no-trade both classes |
| `Ict:Killzones:NewsExtensionEnabled` | `false` | documented deliberate deviation; extends morning window end |
| `Ict:Killzones:NewsExtensionEndTime` | `11:30` |  |
| **Detection: Swing** |  |  |
| `Ict:Detection:Swing:FractalWidth` | `3` | candles (open: 3 vs 5) |
| `Ict:Detection:Swing:StrictInequality` | `true` | equal H/L = liquidity not swing |
| `Ict:Detection:Swing:InvalidateOnCloseBeyond` | `true` | ITH/ITL needs a close |
| **Detection: FVG** |  |  |
| `Ict:Detection:Fvg:MinGapPips` | `1.0` | §2.5.7-c5 placeholder (**I**) |
| `Ict:Detection:Fvg:AtrMultiple` | `1.5` | body ≥ ×ATR (**I**; foundation spec defaulted 0.0 — reconcile to one) |
| `Ict:Detection:Fvg:AtrPeriod` | `14` | (**I**) |
| `Ict:Detection:Fvg:VoidOnTouchCount` | `3` | 3rd tap voids (Ep38) |
| `Ict:Detection:Fvg:MitigateOnFullFill` | `true` |  |
| `Ict:Detection:Fvg:TopDownTimeframes` | `["M15","M5","M3","M1"]` | Intraday default; else from style policy |
| `Ict:Detection:Fvg:RequireInCorrectHalf` | `true` |  |
| `Ict:Detection:Fvg:EquilibriumPercent` | `0.50` |  |
| `Ict:Detection:Fvg:StackProximityPips` | `5` | **I** (foundation fix 2) |
| `Ict:Detection:Fvg:ApplyValidityExclusions` | `false` | flag-only vs hard-reject (open) |
| `Ict:Detection:Fvg:Exclusions:RejectNoSweep` | `true` |  |
| `Ict:Detection:Fvg:Exclusions:RejectInAsianRange` | `true` |  |
| `Ict:Detection:Fvg:Exclusions:RejectCounterBias` | `true` |  |
| `Ict:Detection:Fvg:Exclusions:RejectNoChoch` | `true` | **added** (structure fix 2) |
| `Ict:Detection:Fvg:Exclusions:RejectOverlappingWicks` | `true` |  |
| **Detection: OrderBlock** |  |  |
| `Ict:Detection:OrderBlock:RequireFvg` | `true` | OB invalid without FVG |
| `Ict:Detection:OrderBlock:MeanThresholdPercent` | `0.50` |  |
| `Ict:Detection:OrderBlock:EntryOffsetTicks` | `1` | index |
| `Ict:Detection:OrderBlock:EntryOffsetPipsFx` | `3` | FX |
| `Ict:Detection:OrderBlock:StopBufferPipsFx` | `10` |  |
| `Ict:Detection:OrderBlock:StopBufferTicks` | `2` | index 1–2 ticks |
| `Ict:Detection:OrderBlock:RequireInCorrectHalf` | `true` |  |
| `Ict:Detection:OrderBlock:InvalidateOnCloseThrough` | `true` |  |
| `Ict:Detection:OrderBlock:InvertOnBos` | `true` | PD-array inversion |
| `Ict:Detection:OrderBlock:MaxClusterCandles` | `3` | **I** (open: cluster vs last candle) |
| **Detection: Displacement** |  |  |
| `Ict:Displacement:AtrPeriod` | `14` | (**I**) |
| `Ict:Displacement:AtrMultiple` | `1.5` | (**I**) |
| `Ict:Displacement:MinBodyToRangeRatio` | `0.50` | (**I**) |
| `Ict:Displacement:MinDisplacementPips` | `0` (OFF) | **demoted** (displacement fix 2/3); not a silent AND-clause |
| `Ict:Displacement:DisplacementLegMaxBars` | `3` |  |
| `Ict:Displacement:StructureTimeframe` | from `TimeframePolicy.StructureTf` | not hard-coded |
| **Detection: MSS** |  |  |
| `Ict:MarketStructureShift:CloseBeyondMinPips` | `1.0` | FX / 1 tick (**I**) |
| `Ict:MarketStructureShift:SwingFractalWidth` | `3` |  |
| `Ict:MarketStructureShift:SweepToMssMaxBars` | `5` | (**I**; foundation uses 20 — reconcile) |
| `Ict:MarketStructureShift:RequirePrecedentSweep` | `true` |  |
| `Ict:MarketStructureShift:InvalidationFractalWidth` | `5` | **UNCONFIRMED** (Ep12) |
| **Liquidity / Sweep / Draw** |  |  |
| `Ict:Liquidity:EqualLevelTolerancePips` | `1.5` | **I** (foundation fix 2; consider ATR-relative) |
| `Ict:Liquidity:SwingLookback` | `3` |  |
| `Ict:Liquidity:MinClusterSize` | `2` |  |
| `Ict:Liquidity:RegistryCapacityPerSide` | `32` |  |
| `Ict:Liquidity:ReSweepAllowed` | `false` |  |
| `Ict:Liquidity:BigFigureStepPips` | `100` | (**I**; JPY/index needs per-spec override) |
| `Ict:Liquidity:BigFigureSubLevels` | `[0,20,50,80]` |  |
| `Ict:Liquidity:BigFigureClusterPips` | `5` | (**I**) |
| `Ict:Liquidity:MaxDrawDistancePips` | `120` | (**I**) |
| `Ict:Liquidity:PreferErlOverIrlForT2` | `true` |  |
| `Ict:Liquidity:SweepMinPenetrationPips` | `0.5` | **I** (tie to ATR/tick in WP1) |
| `Ict:Liquidity:UseMacroOpenReference` | `false` |  |
| `Ict:Liquidity:SweepToMssMaxBars` | `5` |  |
| `Ict:Liquidity:SweepRefWindowMinutes` | `120` | (**I**) |
| `Ict:Liquidity:RequireCloseBackInside` | `true` |  |
| `Ict:Liquidity:IncludePriorDayHL` / `IncludeAsianRangeHL` | `true` / `true` |  |
| `Ict:Risk:MinRewardRatio` | `2.5` | configurable; never hard 3R |
| **Bias / DealingRange / PD / OTE** |  |  |
| `Ict:Bias:EquilibriumFib` | `0.50` |  |
| `Ict:Bias:ConsecutiveCloseCount` | `3` |  |
| `Ict:Bias:RequireConsecutiveCloseConfirmation` | **`false`** | flipped (bias fix 2) |
| `Ict:Bias:RequireRecentDailyMss` | `true` |  |
| `Ict:DealingRange:EquilibriumFib` | `0.50` |  |
| `Ict:DealingRange:QuadrantLowFib` / `QuadrantHighFib` | `0.25` / `0.75` | provenance-flagged, non-gating |
| `Ict:DealingRange:AnchorMode` | `BodyToBody` | broken-swing only |
| `Ict:PdGate:PremiumThresholdPct` / `DiscountThresholdPct` | `50.0` / `50.0` |  |
| `Ict:PdGate:InclusiveAtEquilibrium` | `true` |  |
| `Ict:Ote:LowFib` / `HighFib` | `0.62` / `0.79` |  |
| `Ict:Ote:SweetSpotFib` | `0.705` | Primer-sourced; configurable |
| `Ict:Ote:EquilibriumFib` | `0.50` |  |
| `Ict:Ote:AnchorMode` | `BodyToBody` | wick only on FOMC/NFP |
| `Ict:Ote:WickAnchorOnFomcNfp` | `true` |  |
| `Ict:Ote:UseEp41Variant` / `Ep41HighFib` | `false` / `0.70` |  |
| `Ict:Ote:RequirePdArrayOverlap` | `true` |  |
| **Trade styles** |  |  |
| `Ict:TradeStyles:Scalp:{BiasTf,BiasAltTf,StructureTf,StructureAltTf,EntryTf}` | `H1,M15,M5,M3,M1` |  |
| `Ict:TradeStyles:Scalp:MaxHoldMinutes` | `30` | "~30m" approx (open) |
| `Ict:TradeStyles:Scalp:AllowOvernight` | `false` |  |
| `Ict:TradeStyles:Scalp:MinRewardRatio` | `2.5` |  |
| `Ict:TradeStyles:Scalp:AllowDirectFvgEntry` | **`false`** | flipped (style fix 1); Primer-sourced opt-in |
| `Ict:TradeStyles:Intraday:{BiasTf,BiasAltTf,StructureTf,StructureAltTf,EntryTf,EntryAltTf}` | `D1,H4,M15,M5,M5,M1` |  |
| `Ict:TradeStyles:Intraday:MaxHoldMinutes` | `120` | 90–120 band cap |
| `Ict:TradeStyles:Intraday:WarnHoldMinutes` | `90` | no transcript provenance (style fix 2) |
| `Ict:TradeStyles:Intraday:AllowOvernight` | `false` |  |
| `Ict:TradeStyles:Intraday:MinRewardRatio` | `2.5` |  |
| `Ict:TradeStyles:Intraday:AllowDirectFvgEntry` | `false` |  |
| `Ict:TradeStyles:Swing:{BiasTf,BiasAltTf,StructureTf,StructureAltTf,EntryTf}` | `W1,D1,H4,H1,M15` |  |
| `Ict:TradeStyles:Swing:MaxHoldMinutes` | `14400` | =10d, **NOT transcript-stated** |
| `Ict:TradeStyles:Swing:AllowOvernight` | `true` |  |
| `Ict:TradeStyles:Position:{BiasTf,BiasAltTf,StructureTf,EntryTf}` | `MN1,W1,D1,H4` |  |
| `Ict:TradeStyles:Position:MaxHoldMinutes` | `43200` | =30d, **NOT transcript-stated** |
| `Ict:TradeStyles:Position:AllowOvernight` | `true` |  |
| `Ict:TradeStyles:AbsoluteMinRewardRatio` | `2.0` | 2:1 floor; never 3 |
| `Ict:TradeStyles:IntradayClassMaxHoldGuardMinutes` | `1440` |  |
| `Ict:TradeStyles:Classification:DefaultClassificationPriority` | `Timeframe` | Timeframe\|Hold\|Strict (interpretation) |
| `Ict:TradeStyles:Classification:HoldBand{Scalp,Intraday,Swing}MaxMinutes` | `30` / `120` / `14400` |  |
| `Ict:TradeStyles:Classification:RequireOvernightCheck` | `true` |  |
| `Ict:TradeStyles:Classification:TargetDistanceTieEnabled` | `false` |  |
| `Ict:Execution:Swap:CutoffEt` | `17:00` | NY rollover (§5.4) |

Reconcile-before-coding duplicates: `WarmupBars` (200 vs 50), `Fvg:AtrMultiple` (1.5 vs 0.0), `SweepToMssMaxBars` (5 vs 20).

---

## (4) Consolidated `ConfluenceCondition` list — §2.5.3 weights + RequiredConditions

Enum (closed; bind weights/required by member name; `ValidateOnStart` rejects unknown names). Grading: `score = Σ(matched weights)/Σ(applicable weights) ×100`; **A≥80 & all required**, **B 65–79 & all required**, **C 50–64**, **Reject<50**; alert floor 65.

| ConfluenceCondition | Weight | Required? | Emitting detector |
|---|---|---|---|
| `KillzoneEntry` | 1.00 | **Yes** | KillzoneClock |
| `LiquiditySweep` | 0.95 | **Yes** | LiquiditySweepDetector |
| `DisplacementMss` | 0.95 | **Yes** | MarketStructureShiftDetector **(single 0.95 — Displacement is a non-scoring precondition)** |
| `FvgPresent` | 0.90 | **Yes** | FairValueGapDetector |
| `BiasAligned` | 0.85 | **Yes** | DailyBiasDetector |
| `PremiumDiscountHalf` | 0.85 | **Yes** | PremiumDiscountGateDetector **(sole owner; DealingRangeEquilibrium is informational)** |
| `OteZone` | 0.70 | No | OteFibDetector |
| `OrderBlockConfluence` | 0.65 | No | OrderBlockDetector |
| `DrawTargetRrMet` | 0.65 | **Yes** | DrawOnLiquidityDetector |
| `SmtDivergence` | 0.55 | No | (later WP) |
| `OpenPriceReference` | 0.50 | No | (later WP) |
| `MacroTime` | 0.45 | No | SessionMacroDetector (later) |
| `CleanPriceAction` | 0.40 | No | (later WP) |
| `CalendarDriver` | 0.35 | No | (weighted-optional score contributor) |
| **`CalendarClear`** (NEW) | n/a (hard gate) | **Yes** | CalendarGateDetector — the §2.5.2 calendar no-trade block; **distinct from** the 0.35 `CalendarDriver` score contributor (foundation fix 1) |

RequiredConditions set = `{BiasAligned, KillzoneEntry, LiquiditySweep, DisplacementMss, FvgPresent, PremiumDiscountHalf, DrawTargetRrMet, CalendarClear}`. (§2.5.2 "direction matches HTF bias" folds into `BiasAligned`; the calendar clause becomes `CalendarClear`.)

---

## (5) UNRESOLVED ICT issues & per-slice deferral log

Two kinds of entry live here. **Items 1–22 are genuine model ambiguities** — not engineering tunables; resolve them against the transcripts before the affected detector is treated as settled. **Items 23+ are per-slice deferral logs** — architectural cuts with the decision already made and shipped (each cites its issue/PR), recording what a slice deliberately left to a named follow-on so the next session resumes against the real boundary. The two are kept together because both answer "what is NOT yet done and why"; they are distinguished by whether a transcript decision is still owed (1–22) or the scope was already decided (23+).

1. **Calendar gate as required vs weighted (§2.5.2 vs §2.5.3).** Plan resolves to a required hard `CalendarClear` gate. Confirm this is the intended reading and that low-weight `CalendarDriver` (0.35) remains a separate optional score contributor.
2. **FVG two-touch "touch" semantics (Ep38).** Wick-into-gap vs close-into-gap vs full-mitigation step — drives `FvgPresent` withdrawal. Pin with an Ep38-referenced unit test before settling `VoidOnTouchCount=3`. Related: is formation "touch 0" or "touch 1"?
3. **Swing fractal width: 3 vs 5 candles.** §2.5.1 step5 says 3-candle; SKILL "2 either side" reads as 5. Default 3; confirm vs Ep12/Ep41. Also confirm the broken-swing (3) vs ITH/ITL-invalidation (5) pairing and `InvalidationFractalWidth=5`.
4. **OTE canonical band: 62–79% vs Ep41's 62–70%.** Which is authoritative (default 62–79%, Ep41 behind a toggle). Confirm 62%/79% **edge inclusivity** vs Eps 08/19/22/34/38/40/41.
5. **70.5% sweet spot + "3-consecutive-close" bias confirmation.** Both Primer-sourced/provenance-flagged. Is the 3-close confirmation a hard requirement or soft corroborator? (Plan defaults it OFF.)
6. **Premium/discount half anchor: displacement leg vs daily dealing range.** §2.5.1 step6 (leg 50%) vs step1 (dealing-range EQ). Plan uses displacement-leg 50% for the entry-half gate; confirm vs Eps 06/10/25.
7. **Strict-first-FVG vs any-qualifying-FVG (§2.5.10 open).** Affects whether `IsSelectedEntryFvg` is a single selection or multi-eligible, and what "stacked" means.
8. **FVG validity-exclusion enforcement mode.** Hard-reject at detection vs flag-only/down-weight at confluence (`ApplyValidityExclusions`). Default flag-only; confirm intended behavior.
9. **OB candidate: single last opposite-close vs whole down/up-close cluster** (and whether the mean-threshold body spans the cluster). Confirm vs Eps 06/25/26/33.
10. **Reference-open selection (00:00 NY vs 08:30 NY) scope.** 08:30 for Index only, or also FX NY-AM Judas? "Use lower open when bearish" applicability. Confirm Eps 02/03/39.
11. **Multi-candle displacement satisfying MSS.** Single closing-break candle (current spec) vs accumulated multi-candle leg. Confirm Ep05/Ep25.
12. **Sweep→MSS window length** (`SweepToMssMaxBars`) — not in transcripts; reconcile the 5 vs 20 split and confirm a value.
13. **Scalp `AllowDirectFvgEntry` (Silver-Bullet, skip OTE).** Plan defaults FALSE to preserve the §2.5 OTE RequiredCondition. Product/transcript sign-off needed to ever default it true.
14. **DetectedSetupStyleClassifier overrides.** (a) May it relabel a Setup away from the single running `ActiveStyle`? (b) M1 Scalp-vs-Intraday tie-break precedence (`Timeframe` vs `Hold`). Both are interpretations needing sign-off.
15. **Swing/Position hold caps** (10d/30d) and **Scalp ~30m** — transcripts say only "days/weeks/~30m". Keep operator-tunable; no ICT-derived number exists.
16. **Asian killzone as a tradeable entry set** vs the §2.5.10 "inside Asian range" FVG validity exclusion — enabling Asian for entries conflicts with the exclusion. Clarify.
17. **FVG mitigation: full fill vs 50% (consequent encroachment).** Plan uses full fill; add `MitigateAtPercent` only if Eps confirm the 50% variant.
18. **Negative-fib extension targets (−0.27/−0.62/−1.0) vs §2.5.10 SD projections (−1/−1.5/−2 SD).** Affects OTE/target geometry (owned by TargetLadderBuilder, but cross-cuts OTE). §2.5.10 keeps SD primary; confirm.
19a. **OTE invalidation signal (issue #5 fast-follow — adversarial-verify CONCERN).** `OteFibDetector` emits two of the three §2.5.7 void outputs; a previously-in-band FVG/OB that later goes `Mitigated`/`VoidedTwoTouch`/`Inverted` collapses into `OteSkippedNoOverlap`, indistinguishable from "never overlapped". Emit a distinct `OteVoidedOnFvgInvalidation` result/evidence so the FSM + alert audit trail can tell the two apart.
19b. **DealingRange broken-swing anchoring (issue #5 fast-follow / open item 6).** `DealingRangeContextDetector` anchors from active swing extremes; the §2.5.10 ideal consumes the **broken-swing** body-to-body extreme (the Breach/MSS level), which is currently pruned before it can widen the range — so expansion lags by one fractal exactly when bias matters most. Move to broken-swing body-to-body anchoring.
19c. **PremiumDiscount reference-frame decision-log (issue #5).** `PremiumDiscountGateDetector` gates on the **daily dealing range**. If the §2.5.1-step-6 FVG-half check against the **displacement leg** is later added, it MUST be a separate condition/weight so the two reference frames are not conflated under one 0.85 weight. `CalendarGate`: the NFP window is release-anchored (relies on first-Friday), and ingestion must supply the FOMC **announcement** date (not the meeting start).
19. **MSS-vs-SwingPointDetector ordering — RESOLVED (issue #9, PR #9).** Fixed via option (c): `SwingPoint.Breach(breachedAtUtc)` stamps the breaching candle and `MarketStructureShiftDetector.FindBrokenSwing` accepts a swing breached by THIS candle (`WasBreachedOn`) while still excluding earlier-bar-breached / consumed swings. The `ScanSession` pipeline also pins SwingPointDetector before the MSS so the order is deterministic; the same-candle breach is now recognised whether the swing detector runs before or after the MSS (the swing-vs-MSS order specifically — not a claim of full pipeline order-independence). Locked by `MarketStructureShiftDetectorTests` (same-candle + earlier-bar cases) and the end-to-end `ScanSessionTests` pipeline test.
20. **Confluence grading denominator: constant universe vs per-setup subset (issue #9, PR #9 — pr-reviewer Should-fix).** `SetupCandidate` scores against `applicable = the constant set of all weighted conditions` (Σ default weights = 9.75). A consequence of the §2.5.3 default weights: a setup matching ALL 8 RequiredConditions but zero optional confluences scores Σ(required weighted)=6.15 ⇒ **63 ⇒ Grade C**, i.e. **below the 65 alert floor** — so "all required true" is necessary but not sufficient for a B/tradeable alert; some optional confluence (OTE/OB/SMT/macro) is needed to clear 65. This is internally consistent with §2.5.4 as written (B = all-required AND ≥65) and is unreachable on today's pipeline (no `KillzoneEntry` / `DrawTargetRrMet` emitter yet), but it is a genuine model/UX decision: confirm with the transcripts/`ict-domain-expert` whether all-required should auto-clear B (e.g. weight retune, a lower floor, or `applicable` = required∪matched) **before** the alerting/priced-Setup WP, and add a test pinning the bare-required score. §2.5.4. *(Both emitters now exist — issue #11 — so this is reachable on the live pipeline; the decision is due before alerting.)*
22. **Priced Setup aggregate follow-ups (issue #13, PR #13 — pr-reviewer nits).** (a) `SetupFactory` computes T1 at full decimal precision; **tick-round it through `SymbolSpec`** when the fill simulator (WP5) consumes T1, so the partial sits on a tradeable tick. (b) `SetupFactory.Create` throws `DomainException` when the priced frame is below the requested style's RR floor — correct as a single-style invariant, but the multi-style scanning path (WP3) should get a `TryCreate` so "doesn't qualify for this stricter style" is not exception-driven control flow. (c) `ReasonFragments.TradePlanSummary` is an inline template — folds into the domain-wide `.resx` migration (Host/Resources WP), not piecemeal.
21. **DrawOnLiquidity is pools-only + FX-pip stop buffer (issue #11, PR #11 — scoped subset).** `DrawOnLiquidityDetector` draws to **registered untapped liquidity pools only**; the full §2.5.1-step-2 draw set — prior-day H/L (`ctx.DailyRange`), unfilled HTF FVG, big figures (00/20/50/80) — and FVG/OB-anchored or stacked-FVG stops are deferred to the priced-`Setup`/TargetLadder work (WP4/WP5). `DrawOnLiquidityOptions.StopBufferPips` is a single FX-pip default ("~10 pips FX / 1–2 ticks index"); make it **instrument-class-aware** when the index path is wired (WP3/WP7). The swept-level exclusion + strength/`FormedAtUtc` tie-break are output-neutral given the orientation + RR-floor guards (kept as determinism/intent defense), so they carry no dedicated test.

23. **Paper-trade core deferrals (issue #16, PR #17 — WP4 domain core scope).** The `PaperAccount`/`PaperTrade`/`PaperTradeFactory`/`PositionSizer` slice intentionally ships the §5.1 *sizing + open + explicit-close* core and defers: **(a) the adaptive loss-ladder / win-cycle / `IRiskManager`** (§2.4/§2.5.5) — this slice sizes from a flat `RiskOptions.BaseRiskPercent`; the fast-follow must reconcile §2.4's `1%→0.5%`/max-3% with §2.5.5's `1%→0.5%→0.25%`/hard-max-4.5%, the "restore after 50% of the tier dip" semantics, the 5-win→lowest-unit rule, and the win-cycle-first evaluation order (the ict-domain-expert spec's OD-1..OD-5), growing `PaperAccount` with ladder state recorded on `Settle`. **(b) The fill/execution-cost chain** (`Pending→Open` intrabar fills, partial scale-outs, breakeven arming, time-exit, and the spread/commission/slippage/swap `IExecutionCostModel` §5.4) is WP5 — P&L booked here is **GROSS**; `InitialRiskPerUnit` is already frozen so a later stop move keeps R vs the original 1R. **(c)** `ContractSpec.ValuePerPip` is a **static** per-symbol value (USD-quote convention); dynamic account-currency conversion for non-USD-quote / JPY pairs is §5.4/WP5. **(d)** `RiskOptions.MinStopDistancePips` is a single FX-pip floor; make it **instrument-class-aware** (index = ticks) when the index path is wired. **(e)** `PaperAccount`/`PaperTrade` repositories + cross-session persistence are WP2 (the one-settle-per-trade rule is already enforced in-domain via the trade-id ledger); the Host `Ict:Risk` binding + `ValidateOnStart` land with the PaperTrading module (WP3/WP7).

24. **Intrabar fill-evaluator deferrals (issue #18, PR #19 — WP5 exit-leg scope).** `IFillEvaluator`/`FillEvaluator` ships the §5.2 *exit-leg* resolution only — touch-tested (bar High/Low, never close-only, §2.5.8) close at the stop or runner LEVEL, with the conservative `StopFirst` straddle tiebreak applied to BOTH directions (`FillOptions.StopVsTarget`, default worst-case; the raw Open→Low→High→Close path would fill a SHORT's target first and flatter the strategy — deliberately overridden). The evaluator is pure (returns an immutable `FillDecision`, no timestamp → the caller stamps the bar close; `PaperTrade.Close` applies it). Deferred: **(a)** gap-through + spread/slippage worsening — a gapped-through bar fills at the LEVEL here (optimistic by exactly the gap), and the §5.4 `IExecutionCostModel` (spread `level+spread` §2.5.8, commission, slippage tiers, swap, weekend Sunday-reopen gap) degrades it downstream; P&L stays **GROSS** until then. **(b)** T1 **partial scale-outs** + breakeven arming (§2.5.10 BreakEvenAtR) + the trail 50%→25%/75%→BE management + **time-exit** (max-hold 90–120m / no-overnight, named by the trade's `Style`/`Timeframe`) — all need partial-close/stop-move methods on `PaperTrade`; a bar tagging only T1 is **NoFill** today, so the sim is single-full-runner (understates win rate, overstates avg loss vs the real scale-out method — sequence the partials slice next). **(c)** the post-confirmation **Armed/Triggered** (`Pending→Open`) entry-touch fill — `PaperTrade` still opens immediately at plan entry (§5.1); a sibling `IEntryFillEvaluator` evaluates the entry touch later. **(d)** tick/sub-bar **OLHC replay** (the §5.2 gold-standard path) can opt back into a literal intrabar order via a future `IntrabarFillAssumption`. **(e)** `FillOptions` (`Ict:Execution:Fills`) Host binding + `ValidateOnStart` land with the PaperTrading/Scanner wiring (WP3/WP7).

25. **Execution-cost-model deferrals (issue #20, PR #21 — §5.4 slice 1: spread + commission, NET booking).** `IExecutionCostModel`/`ExecutionCostModel` prices the two deterministic always-present FX costs — **round-trip spread** (`2 × BasePips × valuePerPipForPosition`, both legs cross the spread, §5.4) + **commission** (`PerLotRoundTripUsd × lots`, round-turn, once) — into a `TradeCosts`; `PaperTrade.Close` books NET (`GrossPnl`/`Costs`/`NetPnl`, `RealizedPnl`=net, new `NetR`), `RealizedR` stays price-based gross (§5.2), the reserved `RiskBudget` is cost-free. Deferred: **(a)** the **session-stepped spread** (Asian 1.4 / news 6.0 widening) — needs killzone/calendar context on the fill bar; flat base spread is faithful for killzone-gated FX entries (base≈peak there, news vetoed by `CalendarClear`); when added, introduce a spread-model selector. **(b)** **Slippage tiers** (entry/stop, Normal/OffPeak/Stress; stops slip more, never positive; latency) — a fill-PRICE worsening, belongs with the fill layer. **(c)** the §2.5.8 **`level+spread` sweep-fill** worsening stays in `FillEvaluator` (exact-level today) — it is the SAME dollars as the round-trip spread cost line, so when the fill-price form is added one representation must be removed (NEVER both). **(d)** **swap/rollover** (17:00 ET night-counting + triple-Wednesday) — needs `NyClock`; safe to defer ONLY while no trade is held across a 17:00 ET cutoff (the §2.5.9 no-overnight max-hold guarantees 0 nights for Intraday/Scalp); becomes mandatory if Swing/Position styles are enabled (add a closed-trade-never-spans-cutoff assertion to keep the deferral self-policing). **(e)** weekend Sunday-reopen gap; partial fills/ADV/latency; dynamic account-currency conversion for non-USD-quote/JPY (static `ValuePerPip` assumption). `Ict:Execution` Host binding + `ValidateOnStart` land with the PaperTrading module (WP3/WP7).

26. **Trade-management deferrals (issue #22, PR #23 — §2.5.9 Slice A: partial scale-out only; design judge-panel `wf_b072811e-33b`).** Slice A ships the partial scale-out + N-leg R/cost accounting on `PaperTrade` (append-only `FillLeg` ledger; `ScaleOut` books one partial leg + reduces `RemainingSize`; `Close` folds blended totals; R derived from the additive gross fold as `GrossPnl/RiskBudget`, size-weighted vs the frozen 1R; `TradeLifecycle` alongside the unchanged `TradeStatus` so `PaperAccount.Settle` is byte-unchanged; cost-model split into entry/exit legs with a no-double-count regression). Deferred: **(a) Slice B — ✓ SHIPPED (issue #24, PR #25 — see item 27 below for the as-built):** the `MoveStop` ratchet MECHANISM + live `CurrentStop` + `FillEvaluator` re-point + `PaperTradeStopMoved` event landed; the trail-ladder POLICY (50%→25%R, 75%→BE, BE-at-1R, tightest-wins) + its knobs moved on to Slice C, and `BreakevenArmed` became a derived boolean (NOT a lifecycle state — corrected in item 27). **(b) Slice C** — the pure `IExitManager` domain service that folds one candle + a **caller-passed bar-close time** (REQUIRED — `Candle` has only `OpenTimeUtc`, no close time) + a caller-passed NY-date pair into an ordered `ExitPlan` (the stop→scale→time-exit precedence), the max-hold + no-overnight time-exit (reads the style's hold policy), and a `NoOvernightBoundary` enum (NyMidnight default / NyFxClose1700 deferred). **(c)** multiple partials / SD ladder (>1 partial — Slice A books exactly ONE); the §2.5.8 `level+spread` fill-price worsening (stays in `FillEvaluator`, must reconcile with the round-trip spread cost line — same dollars, never both); slippage (§5.5) + swap (§5.6) math; proportional mid-trade reserved-risk release on the partial (the runner keeps the full original `RiskBudget` until final close — conservative); persistence of legs (WP2); `Ict:Execution:Management` Host binding + `ValidateOnStart` (lands with the consuming orchestrator slice). **Open call (spec §5 item 20-adjacent):** a scaled trade reports ONE blended `RealizedR` and must count as ONE sample in `PerformanceCalculator.AvgR` (not one-per-leg) — confirm before the Performance WP.

27. **Stop-trail-mechanism deferrals (issue #24, PR #25 — §2.5.9 Slice B: mechanism only).** Slice B ships the aggregate stop-trail MECHANISM (`MoveStop(newStop, atUtc)` ratchet — tightens toward profit only, may cross entry, may not reach the runner; live `CurrentStop` starting at the frozen `Plan.Stop`; `IsBreakevenArmed` derived bool **orthogonal to** `PartialTaken` — NOT a lifecycle enum value, a deliberate correction of the panel's "BreakevenArmed state"; `PaperTradeStopMoved` event; `FillEvaluator` reads `CurrentStop`; a single `_lastActivityAtUtc` keeps the open→scale-out→stop-move→close timeline monotonic; frozen `RiskBudget`/`RealizedR`-denominator untouched so a breakeven stop books ~0R and a profit-locked stop books +R). Deferred to **Slice C**: the pure `StopTrailPolicy` / `IExitManager` domain service (candle-driven) that DECIDES the new stop from progress and calls `MoveStop` — the §2.5.1-step-9 / §2.5.10 ladder: **T1-half** (progress ≥0.50 of the entry→T1(Partial) range ⇒ stop to 25% residual risk = `Entry − 0.25×InitialRiskPerUnit` long), **T1-three-quarter** (≥0.75 ⇒ breakeven), **break-even-at-1R** (favorable excursion / `InitialRiskPerUnit` ≥ `BreakEvenAtR` 1.0 ⇒ breakeven — a SEPARATE axis from the T1-range fractions, §2.5.10), composed **tightest-wins** (the policy computes the candidate and only calls `MoveStop` when strictly tighter, so it is idempotent across bars); its `StopTrailOptions` (`TrailHalfwayFraction` 0.50 / residual 0.25 / `TrailBreakevenFraction` 0.75 / `BreakEvenAtR` 1.0 / `RequireStructureConfirmForTrail` — configurable defaults, the 1R trigger §2.5.10-sourced/provenance-flagged, bound WITH the policy); the **"don't trail past current price" cap** (candle-aware, belongs in the policy not the aggregate); the **max-hold / no-overnight time-exit** (needs a caller-passed bar-close time — `Candle` has only `OpenTimeUtc` — + a NY-date pair + `NoOvernightBoundary` enum); the §3.4 stop→scale→time-exit precedence. Full build sheet is in the design-panel synthesis (`wf_b072811e-33b`).

28. **StopTrailPolicy deferrals (issue #26, PR #27 — §2.5.9 Slice C cut 1: the trail DECISION only).** Cut 1 ships the pure `StopTrailPolicy.Evaluate(PaperTrade, Candle) → StopTrailDecision` (DECIDE half): two-axis ladder (entry→T1 progress + R-reached vs the **frozen** `InitialRiskPerUnit`, §5.2), tightest-wins, strictly-tighter ratchet pre-filter, the §2.5.8 cap that **Holds** (doesn't clamp or fall back to a looser rung) when the earned stop is inside the bar's adverse range, a belt-and-suspenders not-past-runner guard (unreachable today), `StopTrailOptions` (`Ict:Execution:Management:Trail`) with `RequireStructureConfirmForTrail` reserved off. Adversarially verified (~36k emitted moves, all invariants HOLD vs `MoveStop` + `FillEvaluator`). Deferred to **Slice C cut 2+**: the **`IExitManager` orchestrator** that APPLIES the decision via `MoveStop` (stamping the bar-close time) AND decides the **T1 scale-out** (when to call `ScaleOut` at the partial target) AND folds the §3.4 **stop→scale→time-exit precedence** into one ordered per-candle exit pass; the **max-hold / no-overnight time-exit** (needs a caller-passed bar-close time — `Candle` has only `OpenTimeUtc` — + a NY-date pair + a `NoOvernightBoundary` enum, NyMidnight default); the `RequireStructureConfirmForTrail=true` overlay (the §2.5.1-step-8 "structure broken / time AND price" confirmation — needs `MarketContext`/MSS-continuation, kept out of the pure policy); the SD-ladder rungs beyond the two §2.5.5 rungs; `Ict:Execution:Management:Trail` Host binding + `ValidateOnStart` (lands with the consuming orchestrator).

Relevant existing files to amend, not recreate: `src/IctTrader.Domain/Sessions/NyClock.cs` (IsDst + identity validator), `Styles/TradeStyle.cs` (TimeframePolicy reuse; Daily→D1 mapping), `Sessions/Killzone.cs` (add PM/AM as a flagged WP1 delta), `ValueObjects/OteZone.cs` + `Risk.cs` (reuse as-is), `Setups/SetupGrade.cs` (reuse). New code lands under `src/IctTrader.Domain/Detection/**`, `Detection/Context/MarketContext.cs`, `Resources/{SetupReasons,ValidationMessages}.resx`, and an arch/Roslyn test in `tests/IctTrader.UnitTests`.