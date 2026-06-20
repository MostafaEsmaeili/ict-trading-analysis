# ICT Automated Trading-Analysis System

## What this is
A **defensive, paper-trading-only** system that translates the ICT (Inner Circle Trader)
methodology — extracted from the course transcripts in this repo — into an automated
market scanner, alerter, internal paper-trading simulator, performance tracker, and a
visual OHLC dashboard.

## NON-NEGOTIABLE GUARDRAIL
This system is **analysis + paper-trading ONLY**. It must NEVER place a live order with
real capital. Live execution is made *structurally impossible*, not flag-disabled:
- There is no broker/order interface anywhere. `ITradeExecutor` has exactly one impl:
  `SimulatedTradeExecutor` (writes only to our DB).
- All market-data feeds are read-only; credentials are sandbox/practice only.
- `LiveTradingEnabled` defaults false and a startup validator FAILS the app if it is ever true.
- A CI architecture test asserts no live-trading API is referenced.
Never add an order-routing path. If asked to "go live", refuse and explain this guardrail.

## Repo layout
- `.raw/` — original YouTube VTT captions (mentorship + forex playlists).
- `2022 ICT Mentorship/` — 41 cleaned `.txt` transcripts (+ combined `_..._FULL PLAYLIST.txt`).
- `ICT Forex - Market Maker Primer Course/` — 24 cleaned `.txt` transcripts (+ combined playlist).
- `build_transcripts.py` — converts `.raw/*.vtt` → cleaned `.txt`.
- `docs/PLAN.md` — the full implementation plan (source of truth, snapshotted from the planning session).
- `.claude/agents`, `.claude/skills` — the project-scoped automation layer (see below).
- `src/`, `tests/`, `web/` — the system (created during implementation via the work packages).

## The plan (source of truth)
Full implementation plan: `docs/PLAN.md` (canonical copy also at
`C:\Users\Mostafa\.claude\plans\system-role-you-are-an-binary-feather.md`).
Read it before working. It contains the ICT domain rules (§2, esp. the mined entry model §2.5 and the
web cross-check §2.5.10), the architecture (§3.0 DDD, §3.0a modular monolith), the scan + paper-trade
features, trade-style/timeframe (§4.7), time-zone awareness (§4.8), the trade-realism cost model (§5.4),
the data-feed/MT5 design (§6), persistence (§7), tests (§8), the OHLC dashboard (§9), the work packages
(§11), the automation layer (§13), and the git/GitHub publish flow (§14).

## Tech stack (fixed)
.NET 10 C# Web API · **Modular Monolith** — feature modules decoupled behind an in-memory `IMessageBus`
(**NO MediatR** — it is commercially licensed; we use our own ~3-method bus) · **Domain-Driven Design**
core · group by MODULE then FEATURE, no generic repositories · PostgreSQL + EF Core (JSONB) · SignalR ·
React + TypeScript (Vite) with TradingView **lightweight-charts** for the OHLC pattern chart · E2E tests
with Reqnroll (Gherkin) + Testcontainers for .NET + xUnit.

## Project conventions
- **Self-contained — do NOT depend on any sibling repo.** Minimal-API hosting; Clean Code + SOLID.
- **No magic numbers** → every ICT/trading constant (killzone times, pip sizes, fib levels, risk %,
  spread/commission/slippage/swap, per-style timeframes) lives in `appsettings` under `Ict:*`, bound to
  validated Options POCOs (`ValidateOnStart()`).
- **No magic strings** → all human-facing/log/alert/validation/reason text lives in `.resx` resource files
  (`Resources/`), accessed via a strongly-typed generated accessor; reasons are parameterized templates.
- **DDD is the core discipline (plan §3.0):** ALL business logic in `IctTrader.Domain` — rich aggregates
  with invariants (`PaperTrade`, `PaperAccount`, `Setup`, `ScanSession`), self-validating value objects
  (`Price`, `Pips`, `OteZone`, `RiskPercent`…), domain services (`SetupScorer`, `IRiskManager`,
  `IFillEvaluator`, `IExecutionCostModel`, `PerformanceCalculator`, every `ISetupDetector`), and domain
  events. No anemic models. **No generic repository** — repositories are aggregate-scoped interfaces in
  the Domain. One bounded context, one ubiquitous language (the ICT terms in §2.5).
- **Modular monolith (plan §3.0a):** modules (MarketData, Scanning, PaperTrading, Performance, Alerting,
  Host) talk ONLY via the in-memory bus + each other's `*.Contracts`; no module→module internal references
  (enforced by the architecture tests in `IctTrader.ArchitectureTests`); the bus transport is swappable to
  a distributed broker later.
- `Directory.Build.props`: `net10.0`, `<Deterministic>true</Deterministic>`, nullable enable,
  warnings-as-errors, `<InvariantGlobalization>false</InvariantGlobalization>` (ICU must resolve
  `America/New_York` on any host). Solution is the new `.slnx` format. Line endings are LF, enforced via
  `.gitattributes` so `dotnet format` (`end_of_line = lf`) stays clean on Windows dev + Linux CI.
- **Dependencies — latest stable, license-aware:** every NuGet **and** npm package is pinned to its newest
  stable release, EXCEPT where the latest is commercially licensed — then pin the newest free/OSS version
  and note why (FluentAssertions pinned to **7.x** because 8+ is commercial; MediatR avoided entirely).
  Central package management is off; versions live in each `.csproj`.
- Reference direction: `SharedKernel`/`Domain` depend on nothing; modules → `SharedKernel` + `Domain` +
  others' `*.Contracts`; `Host` → all modules. Bus handlers ORCHESTRATE; the domain DECIDES.
- **Time-zone aware (the host may run anywhere — plan §4.8):** UTC is the only source of truth; never
  `DateTime.Now`/`DateTimeOffset.Now`/`TimeZoneInfo.Local`/the ambient process zone — inject the BCL
  **`TimeProvider`** (`TimeProvider.System` in prod, `FakeTimeProvider` in tests; not a custom `IClock`).
  ALL NY-session math goes through the DST-aware `NyClock` (wrapping `TimeProvider`) using the ICU IANA id `America/New_York`
  (never the Windows id `"Eastern Standard Time"`); a startup validator fails fast if it can't resolve.
  Killzone classification is identical in UTC/Tokyo/Berlin; the dashboard shows NY time by default.
- **Trade-ready realism (plan §5.4):** paper P&L is booked net of spread, commission, slippage, and swap
  via `IExecutionCostModel`; intrabar fills use Open→Low→High→Close so wick-sweeps fill stops honestly.
- **ICT conformance gate:** every change is checked against the ICT model (§2.5/§2.5.10) via the
  `/ict-conformance` skill + the `ict-domain-expert` agent; the §11 Definition-of-Done makes it mandatory.

## Selectable killzone & trade style
- Operator chooses which killzone(s) the scanner hunts via `Ict:Scanning:ActiveKillzones`
  (subset of `Asian | LondonOpen | NewYorkOpen | LondonClose`; default `["LondonOpen","NewYorkOpen"]`).
  ICT preference: London Open (highest odds of the day's high/low) + New York AM.
- `TradeStyle` (Scalp/Intraday/Swing/Position) selects the timeframe triple (Bias/Structure/Entry) from
  the ICT top-down cascade (plan §4.7); default `Intraday` = the §2.5 model. `Ict:Scanning:ActiveStyles`.

## Git/GitHub workflow (the `git-workflow` skill — follow for EVERY change)
- **Issue first** — every change starts from a GitHub issue; its number `N` flows into branch/commits/PR.
- **Branch** — `feature/#N-<kebab-title>` (or `fix|refactor|chore`).
- **Commit title** — `#N <ImperativeVerb> <subject>`, ≤ 72 chars, command mood (Add/Refactor/Fix…),
  never past tense ("Added"). e.g. `#42 Add Trade domain`.
- **Commit body** — wrapped at 80 columns, explains **WHY not WHAT**, ends with
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **PR** — body = *Issue* (what was wrong, `Closes #N`) + *Fix* (how we solved it + how to verify).
- Commit/push only when asked; branch off the default first; never commit secrets.

## Automation layer (`.claude/`, project-scoped — never user-scoped)
- **Agents:** `ict-domain-expert`, `ict-detector-engineer`, `vsa-slice-builder`, `ef-persistence-engineer`,
  `reqnroll-test-engineer`, `react-dashboard-builder`, `defensive-guardrail-auditor`, `pr-reviewer`.
- **Skills:** `ict-methodology` (rules SoT), `add-ict-detector`, `add-vertical-slice`, `mine-ict-transcripts`,
  `verify-ict-system`, `defensive-guardrail-check`, `ict-conformance`, `git-workflow`, `update-memory`.
- **Hooks (`.claude/settings.json`):** PostToolUse → `ict-conformance-reminder.ps1` (nudges `/ict-conformance`
  when `src/**` trading code changes); Stop → `memory-update-reminder.ps1` (reminds to `/update-memory` while
  code changes are pending under `src/`/`tests/`/`web/`).

## Review gate & memory hygiene (mandatory)
- **PR review gate:** before `gh pr create`, run the **`pr-reviewer`** agent on the branch. It checks ICT
  conformance (alignment to §2.5/§2.5.10), the .NET code (**must build with ZERO warnings** — repo is
  warnings-as-errors — `dotnet format` clean, no code smells, DDD/module-boundaries/guardrail, tests pass),
  and the React/TypeScript code (typecheck + lint clean). Fix all **Critical** and **Should-fix** findings
  before opening the PR.
- **Code-review resolution (mandatory):** after acting on any PR review (CodeRabbit's automated review **or**
  a human one), verify each finding against current code, fix the still-valid ones (skip others with a
  one-line reason), re-run the gates, then post **one** summary comment that ends by **tagging
  `@coderabbitai`** with a per-finding (`file:line` Fixed/Deferred) resolution trail. See the `git-workflow`
  skill §5. Never silently ignore a finding.
- **Memory hygiene:** after each period of work / before stopping, run **`/update-memory`** to update this
  `CLAUDE.md` (## Status + any changed convention/command/config) and `docs/PLAN.md` so the next session
  resumes accurately. The Stop hook reminds you while code changes are pending.

## Common commands
- Build (zero warnings): `dotnet build IctTrader.slnx -c Release`
- Format check:  `dotnet format IctTrader.slnx --verify-no-changes`
- Unit tests:    `dotnet test tests/IctTrader.UnitTests`
- Arch tests:    `dotnet test tests/IctTrader.ArchitectureTests` (module boundaries; reflection-based)
- E2E tests:     `dotnet test tests/IctTrader.E2E`   (needs Docker for Testcontainers — WP9)
- Run API:       `dotnet run --project src/IctTrader.Host`
- Run web:       `cd web/ict-dashboard && npm run dev`
- EF migration:  `dotnet ef migrations add <Name> --project src/Modules/<M>/Infrastructure --startup-project src/IctTrader.Host`
- Rebuild transcripts: `python build_transcripts.py <raw_dir> <out_dir> "<Playlist Title>"`

## Build order (see plan §11)
WP0 contracts/skeleton (SharedKernel `IMessageBus` + module shells) → freeze contracts → WP1 detectors +
trade-style / WP2 persistence / WP8 frontend in parallel → WP3 scan → WP4→WP5→WP6 trading chain → WP7
feeds+host+SignalR → WP9 E2E gate. Critical path: 0 → 2 → 4 → 5 → 6 → 7 → 9.

## Domain analysis status — DONE (mined)
Both courses are mined. The 24-episode Market Maker Primer gives the framework (plan §2.1–2.4). The
41-episode 2022 Mentorship (the MAIN course) is mined into **THE entry model** — *ICT 2022 Intraday FVG
Model: Liquidity Sweep → MSS/Displacement → PD-Array OTE Entry* — in **plan §2.5**, web-validated in
§2.5.10 (transcripts remain primary). Re-run the saved workflows (`mine-ict-transcripts` skill) to refresh.

## Status
Planning complete; 2022 Mentorship mined (§2.5) and web-validated (§2.5.10); `.claude/` automation layer +
`CLAUDE.md` created; repo bootstrapped.

**WP0 — DONE & MERGED** (PR #2 → `main`; tag `contracts-v1`). The 22-project modular-monolith solution
(`IctTrader.slnx`) with `SharedKernel` (`IMessageBus` + markers/handlers), the pure `IctTrader.Domain`
primitives, the 5 module Contracts (frozen DTOs + bus messages), and `IctTrader.Host` (frozen REST + SignalR
surface, `DefensiveOptions` live-trading guardrail + `DEFENSIVE MODE` log, `TimeProvider.System`). Reflection-
based architecture tests enforce the boundaries.

**WP1 (issue #3, branch `feature/#3-detection-foundation`) — detection layer in progress.** The pure-domain
ICT detectors encoding §2.5, built TDD with an ICT-verified spec (the `wp1-detector-spec` workflow → adversarial
fidelity pass → [docs/wp1-detector-spec.md]; §5 there lists 19 open ICT decisions on the documented defaults).
Landed (115 unit + 23 architecture tests after the review pass, Release 0 warnings, `dotnet format` clean):
- **Time/session:** `NyClock` (DST via UTC-offset), `KillzoneClock` (instrument-class windows, hard lunch,
  AM cutoff, Asian wrap; `Killzone` extended with `Pm`/`Am`).
- **Confluence engine:** `ConfluenceCondition`, tunable `ConfluenceOptions` (weights/required/thresholds/floor),
  `SetupScorer` (§2.5.4 grading), `DetectorResult`, `EvidenceKeys`, `ReasonFragments`.
- **Market-structure VOs** (rich lifecycle): `SwingPoint`, `FairValueGap`, `OrderBlock`, `LiquidityPool`,
  `Displacement`, `MarketStructureShift`, `DealingRange`.
- **State + contract:** `ISetupDetector`, deterministic `MarketContext` (ring buffers + registries + session/
  bias/sweep/MSS/midnight-open), `SymbolSpec`.
- **Detectors (TDD):** `SwingPointDetector` · `DisplacementDetector` (quantified energy gate) ·
  `LiquidityPoolDetector` + `LiquiditySweepDetector` (sweep≠run, Judas on the penetration) ·
  `MarketStructureShiftDetector` (single `DisplacementMss` 0.95, sweep-must-precede) ·
  `FairValueGapDetector` (**corrected** discount/premium operators, two-touch void, mitigation) ·
  `OrderBlockDetector` (requires linked FVG, mean-threshold, correct-half).
- **Trade style:** `TradeStyleClassifier` + per-style `TradeStyleOptions` (Scalp direct-FVG **off** to preserve
  the OTE RequiredCondition).
- **Everything configurable:** each detector takes a validated Options POCO (`Ict:*`) with verified defaults +
  `Validate()`; pip math via `SymbolSpec`. The Host appsettings **binding + `ValidateOnStart`** wiring lands
  with the scanner module that consumes them (WP3/WP7).

**WP1 review hardening (PR #4, code review resolved):** addressed all 22 CodeRabbit findings + an adversarial
ICT-conformance/guardrail verification pass. Tightened every Options `Validate()` (enum-defined grade,
displacement/FVG/ATR gates, null-safe + frozen-subset `ActiveKillzones`, hard 2:1 floor, throwing
`TradeStyleOptions.For`), made the market-structure VOs self-validate, and corrected the detectors: MSS ignores
inactive/stale swings; `MarketContext` clears intraday state **only on a genuine NY-day rollover** (first candle
initialises, never resets); the OB↔FVG link is timeframe-scoped behind `OrderBlockOptions.RequireSameTimeframeFvg`
(default true, a §2.5.7-deferred proxy for leg membership). **ICT correction over the review:** a sweep close
landing *exactly on* the level stays UNTAPPED (a run is a close *beyond*, §2.5.8) — not consumed. Tests use
`FakeTimeProvider`. Now **138 tests** (115 unit + 23 arch), 0 warnings, `dotnet format` clean. Two deeper items
tracked as WP3 spec work in [docs/wp1-detector-spec.md] §5 (now **19** open items): the MSS-vs-`SwingPointDetector`
ordering race, and true bar-window leg-membership for the OB↔FVG link.

**Convention added:** after acting on any code review (CodeRabbit or human), post one per-finding resolution
summary that **tags `@coderabbitai`** (git-workflow skill §5 + the review-gate section above).

**§2.5 RequiredCondition detectors (issue #3 → #5, PR #8 → merged) — DONE.** Every §2.5.2 RequiredCondition now has an
emitting detector, so the confluence FSM has a complete feeder set. Added (TDD, **172 tests**, 0 warnings, format
clean; `pr-reviewer` APPROVE + adversarial ICT-conformance SHIP 4/5 CONFORMANT):
- **`DealingRangeContextDetector`** (non-scoring) — anchors `MarketContext.DailyRange` from active swing extremes,
  expand-only re-anchor.
- **`DailyBiasDetector`** → `BiasAligned` — discount⇒bullish / premium⇒bearish / equilibrium⇒neutral; 3-close
  corroboration OFF by default; sole writer of `ctx.Bias`.
- **`PremiumDiscountGateDetector`** → `PremiumDiscountHalf` — entry-half veto; emits half-allowed direction,
  non-directional match at an inclusive equilibrium (FSM realises the veto via the direction lock).
- **`OteFibDetector`** → `OteZone` — 62–79% band (sweet spot 70.5%, Primer-flagged) on the pre-validated
  displacement leg; needs a same-direction same-timeframe FVG/OB level in the band.
- **`CalendarGateDetector`** → `CalendarClear` — blocks post-FOMC + the NFP release window from `MarketContext`
  events; NY-date keyed; fail-open when unloaded.
- Shared `EquilibriumBoundaryPolicy` (single 50%-boundary definition), `Sessions/EconomicEvent.cs`, new
  `MarketContext` calendar state (`CurrentNewYorkDate`/`IsCalendarLoaded`/`EconomicEvents`/`LoadCalendar`), five new
  `Ict:Detection:*` Options POCOs. **Two fast-follow issues open:** #6 (OTE distinct `OteVoidedOnFvgInvalidation`
  signal) · #7 (DealingRange broken-swing body-to-body anchoring). Spec §5 now lists **22** open items.

**Confluence FSM (issue #9, branch `feature/#9-confluence-fsm`, PR #9) — DONE.** The per-symbol
`ScanSession`/`SetupCandidate` domain process (pure, `IctTrader.Domain/Setups/`) that folds the detector match
stream into a graded, **advisory-only** `SetupConfirmation`. ICT-faithful (ict-domain-expert spec-reviewed,
`pr-reviewer` APPROVE, **189 tests** = 166 unit + 23 arch, 0 warnings, format clean):
- **MSS owns the direction lock** — the `DisplacementMss` direction locks the trade; an opposing shift *reseeds*
  (intraday reversal = new setup). A condition whose emitted direction contradicts the lock is simply not counted
  → this is how the **premium/discount entry-half veto** is realised (no separate veto flag).
- **Standing vs event conditions** (`SetupCandidateOptions.StandingConditions`, `Ict:Scanning:Candidate`): bias /
  PD / killzone / calendar re-evaluate every candle (so the half-veto withdraws its required match *live*); sweep /
  MSS / FVG / OB / OTE **latch** and age out after `MaxAssemblyBars`. **Sweep must strictly precede the MSS.**
- **Teardown** on anchoring-MSS invalidation (ITH/ITL breach), NY-day rollover, and killzone change; **reset on
  confirm** (no duplicate alert). Grading via `SetupScorer` with `applicable` = the **constant weighted universe**.
- **MSS-vs-swing breach ordering race RESOLVED** (spec §5 item 19, option c): `SwingPoint.Breach(utc)` stamps the
  breaching candle + `WasBreachedOn`; MSS claims a swing breached by THIS candle; `ScanSession` pins
  SwingPointDetector→MSS order. Spec §5 item **20** logs the open grading-denominator/alert-floor decision (a bare
  all-required setup scores 63 < 65 by the §2.5.3 weights — confirm before the alerting WP).
- **Deferred (follow-on):** the post-confirmation **Armed/Triggered** entry-arming + fill states + the priced
  `Setup` aggregate (entry/stop/targets/RR) belong with the paper-trade chain (WP4/WP5); the `KillzoneEntry` +
  `DrawOnLiquidity`/`DrawTargetRrMet` emitters are still missing — two RequiredConditions have no detector — so
  the *default* real pipeline cannot yet reach a confirmation (the FSM itself confirms; the live feeder set is
  incomplete, by design); the distinct OTE-invalidation teardown awaits issue #6.

**Live RequiredCondition feeders (issue #11, branch `feature/#11-killzone-draw-detectors`, PR #11) — DONE.** The
two missing §2.5.2 emitters now exist, so the real ordered pipeline **confirms a graded setup end-to-end** (proved
by a new `ScanSessionTests` integration test). ict-domain-expert spec-reviewed, `pr-reviewer` APPROVE, **207 tests**
(184 unit + 23 arch), 0 warnings, format clean:
- **`KillzoneEntryDetector`** → `KillzoneEntry` (1.0, required) — non-directional time gate; reuses the
  active-entry rule, now extracted to `KillzoneClassification.IsActiveEntryFor` (clock + detector share it). Own
  `KillzoneEntryOptions.ActiveKillzones` (the §4.6 operator hunt-set, frozen-subset validated).
- **`DrawOnLiquidityDetector`** → `DrawTargetRrMet` (0.65, required) — direction from the **confirmed bias-aligned**
  MSS; entry = the shared OTE level; stop beyond the swept extreme + pip buffer (orientation asserted); target =
  nearest **untapped opposite-side** pool beyond entry (sweep one side, draw to the other), excluding the
  just-swept level and HRLR runs; RR floor = the active style's `MinRewardRatio` clamped by
  `AbsoluteMinRewardRatio` (**no new RR knob**); `RewardRatio` VO + zero-risk guard. `DrawOnLiquidityOptions`.
- **Shared `OteEntryResolver`** — extracted from `OteFibDetector` (pure refactor, no behavior change) so the OTE
  entry can't drift between the OTE and draw detectors.
- **Scoped/deferred (spec §5):** the draw targets **registered pools only** — the broader §2.5.1-step-2 set
  (prior-day H/L, HTF FVG, big figures) + stacked/array-anchored stops are the priced-`Setup` (WP4/WP5) work.

**Priced `Setup` aggregate (issue #13, branch `feature/#13-setup-aggregate`, PR #13) — DONE.** The confirmed,
advisory, **priced** setup (`IctTrader.Domain/Setups/`) the Alerting + PaperTrading modules consume.
ict-domain-expert spec-reviewed, `pr-reviewer` APPROVE, **217 tests** (194 unit + 23 arch), 0 warnings, format clean:
- **`Setup`** aggregate root — grade gate (**only A/B** become a Setup; C/Reject never do), structurally
  `IsAdvisoryOnly`, carries the `TradePlan` + `SetupReason` + `TradeStyle`/`Timeframe` (which name the
  max-hold/no-overnight policy the simulator applies — not mutable management fields).
- **`TradePlan` + `TargetLadder`** VOs — total-order invariant `stop < entry < T1 < T2` (mirror for short); RR
  recomputed entry→runner from geometry. T2 = the **exact** draw; T1 = the entry→T2 **equilibrium**
  (`TargetLadderOptions.T1EquilibriumFraction`, §2.5.5 50%); two tiers, SD ladder deferred.
- **`PricedFrame`** — the FSM captures entry/stop/target/RR from the `DrawTargetRrMet` evidence onto the
  `SetupConfirmation`, so `SetupFactory` prices the plan against the **frozen gated draw** (never re-derives it).
- **`SetupFactory`** — builds the Setup, re-checks the style RR floor (belt-and-suspenders), composes the reason
  (rank-sorted clauses + a priced `TradePlanSummary`). The end-to-end `ScanSessionTests` now prices a Setup from
  the real pipeline.
- **Deferred (pr-reviewer nits / spec §5):** tick-round T1 in the fill layer; a `TryCreate` for multi-style
  orchestration (so "doesn't qualify for this style" isn't exception-driven); the §2.5.7 management fractions +
  Armed/fill stay WP5.

**Paper-trade core — `PaperAccount`/`PaperTrade` aggregates (issue #16, branch `feature/#16-paper-trade-aggregates`,
PR #16) — DONE.** The WP4 domain core (`IctTrader.Domain/Trading/`): a confirmed advisory `Setup` becomes ONE sized
`PaperTrade` opened against a `PaperAccount` (the FIRST consumer of `Setup`). ict-domain-expert spec-reviewed,
`defensive-guardrail-auditor` PASS, `pr-reviewer` APPROVE (both Should-fix items hardened), **247 tests** (224 unit +
23 arch), 0 warnings, format clean:
- **VOs:** `Money` (signed account-currency, arithmetic + comparison), `PositionSize` (lots > 0), `ContractSpec` (the
  MONEY geometry — value-per-pip / lot-step / min-lot — deliberately separate from `SymbolSpec`'s price geometry so
  price-only detectors stay money-free; `FxMajor` default 10/pip, 0.01 step+min).
- **`PositionSizer`** — the pure §5.1 chain (`riskPerUnit → stopPips → riskAmount → floor-to-lot-step qty`); floors
  DOWN so realized risk ≤ budget; min-stop-distance guard (~10 pips FX) + min-lot guard (never a 0-lot trade).
- **`PaperAccount`** aggregate root — equity + an **open-trade ledger keyed by trade id**: `RegisterOpen`/`Settle` are
  account-scoped + **idempotent** (no double-reserve / double-settle / cross-account), the aggregate **open-risk cap**
  (`MaxOpenPortfolioRiskPercent` ≈5%, §2.5.10) gates admission, settlement releases the trade's reserved risk + books
  gross P&L and keeps equity positive.
- **`PaperTrade`** aggregate root — opens at the plan entry (immediate, §5.1), **freezes `InitialRiskPerUnit`** so R
  is always vs the original 1R (§5.2), and **derives its own `RiskBudget`** from the same geometry it books P&L with,
  so the reserved risk and a stop-out loss can never disagree; `Close` realizes signed R + gross P&L; raises
  `PaperTradeOpened`/`PaperTradeClosed` (the FIRST domain events — `AggregateRoot` event infra now exercised).
  `TradeStatus`/`TradeCloseReason` lifecycle enums.
- **`PaperTradeFactory`** — `Setup` + `PaperAccount` + `SymbolSpec` + `ContractSpec` → sized, cap-checked, **atomically**
  opened `PaperTrade` (a refused trade leaves the account untouched); direction inherited from the bias-aligned Setup
  (counter-bias structurally impossible).
- **`RiskOptions`** (`Ict:Risk`): `BaseRiskPercent` 1.0 / `MaxOpenPortfolioRiskPercent` 5.0 / `MinStopDistancePips` 10,
  all `Validate()`-gated (per-trade risk ≤ portfolio cap).
- **Deferred (spec §5 item 23):** the adaptive **loss-ladder + win-cycle + `IRiskManager`** (§2.4/§2.5.5 — this slice
  is flat base risk); the **fill/execution-cost chain** (Pending→Open intrabar fills, partials, breakeven, time-exit,
  spread/commission/slippage/swap §5.4 — P&L here is **GROSS**) = WP5; static value-per-pip (dynamic FX conversion
  later); instrument-class min-stop when index is wired; repositories = WP2; Host `Ict:Risk` binding lands with the
  PaperTrading module.

**WP4/WP5 still to come (next slice):** the post-confirmation **Armed/Triggered** + realistic **fill/execution-cost
chain** (the next consumer of `PaperTrade` — intrabar Open→Low→High→Close fills, partials, breakeven, time-exit,
§5.4 cost model), then the adaptive **loss-ladder/`IRiskManager`** fast-follow + the `Performance` calculator (WP6);
the extended/long-tail detectors (SMT, Breaker, SD projection, session macros). Then WP2 (persistence) / WP8
(frontend) in parallel. Spec §5 item **20** (grading denominator / alert floor) still needs a call before alerting.
