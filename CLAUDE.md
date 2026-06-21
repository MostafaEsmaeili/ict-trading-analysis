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
PR #17) — DONE.** The WP4 domain core (`IctTrader.Domain/Trading/`): a confirmed advisory `Setup` becomes ONE sized
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

**WP5 intrabar fill evaluator (issue #18, branch `feature/#18-fill-evaluator`, PR #19) — DONE.** The first WP5 cut:
a PURE domain `IFillEvaluator`/`FillEvaluator` (`IctTrader.Domain/Trading/`, plan §5.2) that folds one `Candle` over one
OPEN `PaperTrade` into a `FillDecision` (close at stop / runner, or no fill) — so the simulator finally decides WHERE a
trade leaves the market. ict-domain-expert spec-reviewed, `pr-reviewer` APPROVE (no Critical/Should-fix),
`defensive-guardrail-auditor` 7/7 PASS, **265 tests** (242 unit + 23 arch), 0 warnings, format clean:
- **Touch tests read bar HIGH/LOW, never close-only** (§2.5.8) — long stop `Low≤stop` / TP `High≥runner`, short mirror;
  inclusive boundary (an exact kiss fills). An ICT wick-sweep that closes back inside still stops the trade out.
- **Resting orders fill at the LEVEL** (stop / runner price), so a stop-out books exactly **−1R** and a runner books the
  **plan RR** (proved by applying the decision to `PaperTrade.Close`).
- **Straddle tiebreak = conservative `StopFirst` for BOTH directions** (`FillOptions.StopVsTarget`, default worst-case) —
  deliberately overriding the raw Open→Low→High→Close path, which would optimistically fill a SHORT's target first and
  flatter the strategy. `IntrabarFillAssumption { StopFirst, TargetFirst }`; `TargetFirst` is a what-if escape hatch.
- **Pure / DECIDE-vs-APPLY** — the evaluator returns an immutable `FillDecision` (no timestamp → clock-free, the caller
  stamps the bar close); `PaperTrade.Close` applies it. `FillOptions` (`Ict:Execution:Fills`, `Validate()`-gated, wired
  into `OptionsValidationTests`). No magic numbers.
- **Deferred (spec §5 item 24):** gap-through + spread/slippage worsening (§5.4 cost model — P&L still **GROSS**); T1
  partial scale-outs + breakeven arming + time-exit (need partial-close/stop-move on `PaperTrade`); entry-arming
  (Pending→Open); tick/sub-bar OLHC replay. `FillOptions` Host binding lands with the PaperTrading/Scanner wiring.

**WP5 execution-cost model — paper P&L booked NET (issue #20, branch `feature/#20-execution-cost-model`, PR #21) —
DONE.** The first §5.4 cut: a PURE `IExecutionCostModel`/`ExecutionCostModel` (`IctTrader.Domain/Trading/`) prices the
two deterministic always-present FX costs into a `TradeCosts`, and `PaperTrade.Close` now books NET. ict-domain-expert
spec-reviewed, `pr-reviewer` APPROVE (no Critical/Should-fix), `defensive-guardrail-auditor` 7/7 PASS, **275 tests**
(252 unit + 23 arch), 0 warnings, format clean:
- **Round-trip spread** = `2 × BasePips × valuePerPipForPosition` (cross the spread on BOTH legs, §5.4) + **commission**
  = `PerLotRoundTripUsd × lots` (round-turn, levied once). Both read the trade's OWN money geometry
  (`PaperTrade.ValuePerPipForPosition`, now exposed) so a cost can never disagree with booked P&L.
- **`PaperTrade.Close(exitPrice, reason, TradeCosts, closedAtUtc)`** — signature now takes the computed costs (the model
  DECIDES, `Close` APPLIES). Exposes `GrossPnl` / `Costs` / `NetPnl`; `RealizedPnl` = **net** (what `PaperAccount.Settle`
  books); `RealizedR` stays the **price-based gross R** (§5.2 frozen-1R — a stop-out is still exactly −1R gross) + a new
  `NetR = NetPnl / RiskBudget`. The reserved `RiskBudget` (price-based stop risk) is **unchanged** by costs.
  `PaperTradeClosed` now carries RealizedR + NetR + GrossPnl + Costs + NetPnl (for the §5.3 perf views). 13 existing
  `Close` call sites pass `TradeCosts.Zero` (gross-geometry tests stay valid: net == gross when costs are zero).
- **`ExecutionCostOptions`** (`Ict:Execution` → `Spread.BasePips` 0.7 / `Commission.PerLotRoundTripUsd` 6.0), `Validate()`
  -gated, wired into `OptionsValidationTests`. Flat base spread is faithful for killzone-gated FX entries (base≈peak
  there; the news minute is already vetoed by `CalendarClear`). No magic numbers; `RoundTripLegs` is a named const.
- **Spread is the COST LINE here; the §2.5.8 `level+spread` fill-price worsening stays in `FillEvaluator` (deferred) so
  the same dollars are never double-counted.**
- **Deferred (spec §5 item 25):** session-stepped spread (+ its model selector); **slippage tiers**; **swap/rollover**
  (17:00 ET night-counting + triple-Wednesday — safe to defer while the no-overnight max-hold holds → 0 nights); weekend
  gap; partial fills/latency; dynamic account-currency conversion. `Ict:Execution` Host binding lands with the
  PaperTrading module.

**WP5 partial scale-out — `PaperTrade` runner + N-leg R/cost accounting (issue #22, branch `feature/#22-partial-scale-out`,
PR #23) — DONE.** Slice A of the §2.5.9 management model (scope chosen by a **design judge-panel workflow** — `Explore`
map + ict-domain-expert spec → 3 competing aggregate designs → adversarial judging → synthesis: ship the runner, defer the
trail/time-exit). `pr-reviewer` APPROVE, `defensive-guardrail-auditor` 7/7 PASS, an **adversarial money-math verifier**
caught a fragile exact-equality identity test (fixed), **293 tests** (270 unit + 23 arch), 0 warnings, format clean:
- **`PaperTrade` closes through an append-only `FillLeg` ledger** — an optional `ScaleOut(exitPrice, legSize, legCosts,
  reason, atUtc)` books ONE partial leg over part of the size and reduces `RemainingSize`; `Close` (same signature) books
  the final leg. `RealizedR`/`GrossPnl`/`Costs`/`RealizedPnl`/`NetR` are **derived folds** over the legs (one source of
  truth → illegal state unrepresentable). `FillLeg` (Lots/ExitPrice/Reason/Costs/AtUtc), `HasScaledOut`.
- **R is blended size-weighted vs the FROZEN 1R** (0.5@+1R + 0.5@+3R = **+2.0R**). Anti-drift: `GrossPnl` is the additive
  money fold, `RealizedR = GrossPnl / RiskBudget` is **derived from it** (never both independently). `RealizedR` stays the
  structural gross edge; `NetR` carries costs. The no-partial path is byte-identical to a single full close.
- **`TradeLifecycle { Open, PartialTaken, Closed }`** rides ALONGSIDE the unchanged `TradeStatus { Open, Closed }` ⇒
  `PaperAccount.Settle` is **byte-unchanged** and still rejects a not-yet-final (`PartialTaken`) trade; the partial never
  touches the account (money lands on equity in the single terminal settle, full `RiskBudget` reserved until then).
  `PaperTradePartialClosed` event carries the derived per-leg figures.
- **Cost-model split:** `ComputeEntryLeg` (one spread crossing, no commission) + `ComputeExitLeg(legSize)` (one crossing +
  per-lot commission on that leg); `Compute` re-expressed as entry + full-exit. A regression test locks
  `entry + exit(Size) == legacy round trip`, so a partial + runner **never double-count the spread**.
- **`ExitManagementOptions`** (`Ict:Execution:Management` → `PartialFraction` 0.50, **flagged INVENTED** — transcripts say
  take a partial at T1 but never the size), `Validate()`-gated. (Defined now; the orchestrator that consumes it is Slice C.)
- **Deferred (spec §5 item 26):** **Slice B** = `MoveStop`/`CurrentStop` + the 50%→25%/75%→BE/BE-at-1R trail (FillEvaluator
  re-pointed to read `CurrentStop`) + `PaperTradeStopMoved` + `BreakevenArmed`; **Slice C** = the pure `IExitManager`
  orchestrator + max-hold/no-overnight time-exit (needs a caller-passed bar-close time — `Candle` has only `OpenTimeUtc`);
  plus multiple partials / SD ladder, the §2.5.8 `level+spread` fill-price worsening, slippage/swap math.

**WP5 stop-trail mechanism — `PaperTrade.MoveStop`/`CurrentStop` (issue #24, branch `feature/#24-stop-trail-mechanism`,
PR #25) — DONE.** Slice B of the §2.5.9 management (mechanism only; the candle-driven trail-ladder POLICY is Slice C).
ict-domain-expert spec-reviewed, `pr-reviewer` APPROVE, `defensive-guardrail-auditor` 7/7 PASS, an **adversarial verifier**
drove the built DLL with ~35 probes (all 6 invariants HOLD) and flagged a timeline gap + coverage gaps (both addressed),
**314 tests** (291 unit + 23 arch), 0 warnings, format clean:
- **`MoveStop(newStop, atUtc)` ratchet** — tightens toward profit ONLY (long up / short down, strict), may cross entry to
  lock profit, may NOT reach the runner target. Mutates only `CurrentStop` (live stop, starts = `Plan.Stop`); the frozen
  `Stop`/`InitialRiskPerUnit`/`RiskBudget`/`RealizedR`-denominator are untouched, so R is still vs the original 1R (§5.2):
  a breakeven stop-out books **~0R** (not −1R), a profit-locked stop books **positive R** on a pullback.
- **`FillEvaluator` now reads `CurrentStop`** (was `Plan.Stop`), so a trailed stop actually governs the exit fill.
- **`IsBreakevenArmed`** is a DERIVED boolean (stop at/beyond entry in the profit direction) — orthogonal to the partial
  state (a trade can be both `PartialTaken` and breakeven-armed), NOT a lifecycle enum value (corrected the panel here).
  `MoveStop` raises `PaperTradeStopMoved` (prev/new stop + breakeven snapshot).
- **Monotonic timeline:** a single `_lastActivityAtUtc` (open→scale-out→stop-move→close) guards every stamped op (closed
  the adversarial-flagged gap where a stop move could predate a later scale-out; money was never at risk, P&L is
  price-derived). Clock-free (caller passes the timestamp).
- **Deferred (spec §5 item 27 / Slice C):** the pure `StopTrailPolicy`/`IExitManager` that DECIDES the new stop from
  per-candle progress (the 50%→25%R / 75%→BE / BE-at-1R ladder, "tightest-wins", entry→T1 progress vs the 1R-reached
  axis) + its `StopTrailOptions`; the max-hold/no-overnight time-exit (needs a caller-passed bar-close time); the
  "don't trail past current price" cap (belongs in the candle-aware policy).

**WP5 stop-trail policy — `StopTrailPolicy` candle-driven decision (issue #26, branch `feature/#26-stop-trail-policy`,
PR #27) — DONE.** The first cut of §2.5.9 Slice C: the pure DECIDE half of the trail ladder (the orchestrator that
applies it, the scale-out policy, and the time-exit stay deferred). ict-domain-expert spec-reviewed, `pr-reviewer`
APPROVE, `defensive-guardrail-auditor` 7/7 PASS, an **adversarial verifier** drove the built DLL with **~36k emitted
moves** (all 6 invariants HOLD, cross-checked vs `MoveStop` + `FillEvaluator`), **328 tests** (305 unit + 23 arch), 0
warnings, format clean:
- **`StopTrailPolicy.Evaluate(PaperTrade, Candle) → StopTrailDecision`** (pure, clock-free, reads candle High/Low only).
  Two axes off the bar's favorable excursion: entry→T1 progress (≥50% → a **residual-risk** stop `Entry∓0.25×1R`; ≥75%
  → breakeven) and **R-reached** vs the **FROZEN** 1R (`BreakEvenAtR` 1.0 → breakeven), composed **tightest-wins**. R
  divides by `InitialRiskPerUnit`, NEVER the live `CurrentStop` (the §5.2 frozen-1R rule — a trailed stop never shrinks
  the denominator).
- **Strictly-tighter ratchet pre-filter** (never emits a move `MoveStop` would reject) + a belt-and-suspenders
  not-past-runner guard (unreachable by today's rungs; protects a future SD-ladder rung). The **§2.5.8 cap** rejects any
  candidate the bar already traded through (`< candle.Low` long / `> candle.High` short — consistent with the fill
  evaluator's inclusive touch) and **Holds** (waits for a clean bar) rather than clamping/falling back to a looser rung.
- **`StopTrailDecision`** mirrors `FillDecision` (`Hold` / `Move(newStop, trigger)`, `StopTrailTrigger`
  {T1HalfResidualRisk, T1ThreeQuarterBreakeven, BreakevenAtOneR} — the 1R axis wins the BE tie-break label).
- **`StopTrailOptions`** (`Ict:Execution:Management:Trail`): `TrailHalfwayFraction` 0.50 / `TrailHalfwayResidualRiskFraction`
  0.25 / `TrailBreakevenFraction` 0.75 (§2.5.5 primary) + `BreakEvenAtR` 1.0 (§2.5.10 addition, provenance-flagged) +
  `RequireStructureConfirmForTrail` (default off — the §2.5.1-step-8 "structure broken" overlay reserved as a seam),
  `Validate()`-gated (`halfway < breakeven`) + wired into `OptionsValidationTests`.
- **Deferred (spec §5 item 28):** the `IExitManager` orchestrator that APPLIES the decision via `MoveStop` + folds the
  stop→scale→time-exit **precedence**; the scale-out policy (when to take T1); the **max-hold/no-overnight time-exit**
  (needs a caller-passed bar-close time + NY-date pair + `NoOvernightBoundary` enum); the `RequireStructureConfirmForTrail
  =true` behavior (needs `MarketContext`/MSS continuation); the SD-ladder rungs.

**WP5 exit orchestrator — `IExitManager` per-candle pass (issue #28, branch `feature/#28-exit-manager-orchestrator`,
PR #29) — DONE.** Slice C cut 2a: the orchestrator that finally RUNS the exit machinery per candle (the max-hold/
no-overnight time-exit is cut 2b). ict-domain-expert spec-reviewed, `pr-reviewer` APPROVE, `defensive-guardrail-auditor`
7/7 PASS, an **adversarial verifier** drove **505k Decide-then-Apply cycles** (all 6 invariants HOLD, 0 throws),
**341 tests** (318 unit + 23 arch), 0 warnings, format clean:
- **`ExitManager.Decide(PaperTrade, Candle, ExitContext) → ExitPlan`** runs the §3.4 precedence **protective-fill →
  scale → trail**: a stop/runner that hit closes the WHOLE remaining position and emits nothing else that bar
  (delegates to `FillEvaluator` — never re-derives the fill); otherwise a surviving bar takes the **T1 partial ONCE**
  (`Lifecycle==Open` guard, sized `PartialFraction × the ORIGINAL Size`, booked at the partial LEVEL via
  `ComputeExitLeg`) and ratchets the stop if `StopTrailPolicy` earned a tighter level.
- **DECIDE-only** (mirrors `FillEvaluator`/`StopTrailPolicy`): returns an immutable **apply-ordered** `ExitPlan` of
  `ExitAction`s (scale BEFORE move-stop, both stamped at the caller-passed bar-close time so the aggregate's monotonic
  timeline holds); the caller APPLIES it. Pure/clock-free — the bar-close time arrives via `ExitContext` (self-validating
  UTC). `ExitPlan.NoOp`/`HasActions` (null-safe). An integration test drives a real trade scale→runner to **+1.875R**.
- **Deferred (spec §5 item 29 / cut 2b+):** the **max-hold / no-overnight time-exit** — it alone pulls in `NyClock`,
  a `NoOvernightBoundary` enum (NyMidnight default), `TimeframePolicy.MaxHold`, and the bar-close time already on
  `ExitContext`; it inserts as **protective-fill → time-exit → scale → trail** (time-exit overrides a same-bar
  scale/trail but never a real fill), closing at `candle.Close` with `TradeCloseReason.TimeExit`. Also deferred:
  **lot-step flooring** of the partial leg (with the multi-partial/SD-ladder work); the `RequireStructureConfirmForTrail
  =true` overlay; the Host `Ict:Execution:Management*` binding + `ValidateOnStart` (with the consuming host wiring).

**WP5 exit time-exit — `ExitManager` max-hold / no-overnight (issue #31, branch `feature/#31-time-exit`, PR #32) —
DONE.** Slice C cut 2b: the §2.5.1-step-9 *"max hold 90–120 min; no overnight"* rule woven into the orchestrator as the
second precedence rung. ict-domain-expert spec-reviewed (Mentorship-verbatim — Ep21 "90 min to 2h maximum", Ep2 "00:00
NY new day"); an **aggressive 4-lens verification workflow** all SHIP — strict ICT fidelity (12/12 checklist), an
**adversarial driver over the compiled domain (~275k cases × 4 styles × both 2024 DST transitions × 5 process zones,
ZERO invariant violations)**, `defensive-guardrail-auditor` 7/7, `pr-reviewer` APPROVE. **356 tests** (333 unit + 23
arch), 0 warnings, format clean:
- **Precedence is now `protective-fill → time-exit → scale → trail`.** A real stop/runner fill on the max-hold bar
  ALWAYS wins (books the level −1R / plan RR, never a flattering bar-close `TimeExit`) — the safety-critical honesty
  rule; the time-exit OVERRIDES a same-bar scale and trail (no remaining position to manage once flattening).
- **`ExitManager.TimeExitFires`** = **max-hold** (`BarClose − Open ≥ StyleSettings.MaxHoldMinutes`, pure UTC, inclusive;
  Intraday 120 — read from existing per-style config, NO new literal) **OR** **no-overnight** (`NyClock.NewYorkDate(open)
  != NyClock.NewYorkDate(barClose)`, the only NY-date path, §2.1 00:00-NY boundary), gated on `StyleSettings.AllowOvernight
  == false` so only intraday-class styles force out (Swing/Position don't). Both → ONE whole-`RemainingSize` `Close` at
  `candle.Close` as `TimeExit`, costed via `ComputeExitLeg`; R still vs the frozen 1R. Pure/clock-free (NyClock date
  conversion never reads `UtcNow` on this path).
- **`NoOvernightBoundary { NyMidnight (default), NyFxClose1700 (deferred) }`** on `ExitManagementOptions`
  (`Ict:Execution:Management`). The FX-close boundary is **double-blocked** — rejected by `Validate()` AND throws
  `NotSupportedException` if ever reached — so an operator can't silently get unimplemented behavior. `ExitManager` ctor
  now injects `NyClock` + `TradeStyleOptions` (only construction site is the test; Host DI wiring still deferred).

**Concurrency note:** this session ran two parallel teams — the backend domain track above (cut 2b) and a **frontend
team scaffolding WP8** (the ICT Pattern Chart + 3 panels + DTO types mirrored from the frozen contracts) in an isolated
git worktree. ICT/domain correctness is the hard gate and was verified strictly (the user directive); the frontend is
independent and cannot touch the domain.

**WP5 still to come (next slice):** the post-confirmation **Armed/Triggered** (Pending→Open) entry-arming, the
**slippage** + **session-stepped spread** + **swap** cost follow-ons (swap becomes mandatory only if Swing/Position are
enabled — the no-overnight time-exit now structurally guarantees 0 nights for Intraday/Scalp), the adaptive
**loss-ladder/`IRiskManager`** fast-follow, lot-step **flooring** of the partial leg, and the `Performance` calculator
(WP6); the extended/long-tail detectors (SMT, Breaker, SD projection, session macros). Then WP2 (persistence) / **WP8
(frontend — scaffold in progress, parallel team)** in parallel. Spec §5 item **20** (grading denominator / alert floor)
still needs a call before alerting.
