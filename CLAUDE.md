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

**WP5 entry evaluator — `EntryFillEvaluator` (issue #33, branch `feature/#33-entry-fill-evaluator`, PR #35) — DONE.**
Cut 1 of the post-confirmation **Armed/Triggered entry-arming**: the pure DECIDE half of the §2.5.1-step-7 limit-touch
entry, mirroring how `FillEvaluator` (the exit touch) shipped before the `ExitManager` orchestrator. ict-domain-expert
spec-reviewed (Mentorship FULL PLAYLIST:876-877 "limit order where FVG/OB coincides with OTE"; :933 "may not get
filled"; :2817/:3097 "don't chase"), an **aggressive 4-lens verification** all SHIP — strict ICT fidelity, an
**adversarial driver (310k cases, all 7 invariants HOLD, validated by a negative control that flagged 11,418 violations
on a reversed operator)**, `defensive-guardrail-auditor` 7/7, `pr-reviewer` APPROVE. **367 tests** (344 unit + 23 arch),
0 warnings, format clean:
- **`EntryFillEvaluator.Evaluate(Setup, Candle) → EntryFillDecision`** (pure, clock-free). ICT enters on a resting
  LIMIT at the OTE/FVG level — price must RETRACE in: long fills `candle.Low ≤ plan.Entry` (the **discount-side** touch
  — looks like a long STOP operator but is the limit retrace, the verifier's load-bearing fidelity check), short fills
  `candle.High ≥ plan.Entry` (premium); bar High/Low touch, inclusive (§2.5.8). A no-retrace setup is a no-fill.
- **Fills at the LIMIT LEVEL** (`plan.Entry`), never the better gap price, so planned 1R (|entry−stop|) == booked 1R.
  The buy@ask/sell@bid spread stays the §5.4 `ComputeEntryLeg` cost line — NOT a fill-price worsening here — so the same
  dollars are never double-counted (mirrors the exit `FillEvaluator`). `EntryFillDecision { Hold, Filled }` (+
  `FillPrice`/`IsFilled`) mirrors `FillDecision`. No aggregate/account changes; `Setup`/`PaperTrade` untouched.
- **Deferred (spec §5 item 31):** the same-bar **entry-then-stop −1R straddle** (a fast bar fills the limit THEN runs to
  the stop — the orchestrator MUST resolve it worst-case before the next-bar exit pass, else it books a phantom
  favorable outcome — flagged by the verifier; a future `EntryFillDecision` may carry a stop-also-touched flag); the
  `ArmedEntry` lifecycle + no-chase cancellation (killzone-end / setup-invalidation / no-overnight / assembly-ageout);
  **risk reservation at arm time** (id-keyed on `PaperAccount`); `EntryMode { Armed (default), Immediate }`; the
  orchestrator that APPLIES the fill. The ICT spec flagged **2 contested sub-decisions** for that cut (risk-reservation
  wiring, straddle boundary) → resolve via a **design judge-panel**.

**WP5 entry-arming cut 2a — arm-time reservation + `ArmedEntry` + `OpenArmed` (issue #36, branch
`feature/#36-armed-entry-reservation`, PR #37) — DONE.** Scoped by a **design judge-panel** (3 designs → 3 adversarial
judges → synthesis; **Design 1 won unanimously 9/9/9**). The arm-time reservation MECHANISM (the per-candle orchestrator
+ no-chase cancellation + the same-bar straddle are cut 2b). ict-domain-expert + `defensive-guardrail-auditor` 7/7 +
`pr-reviewer`; an **adversarial driver drove ~92k cases (all 6 invariants HOLD, 0 drift, negative-control-validated)**;
all reviewer findings hardened. **378 tests** (355 unit + 23 arch), 0 warnings, format clean:
- **`PaperAccount.Reserve(Guid, Money)`** — id-keyed, extends the **existing** `_reservedRiskByTrade` ledger, so a
  resting limit is committed exposure competing for the SAME ~5% cap via the SAME `OpenRisk`/`CanOpen` gate (§2.5.10) —
  **one cap owner** (reserve-at-trigger and a separate `ArmedBook` were rejected: cap-breach window / split invariant).
  Throws-without-mutating on empty/duplicate id or cap breach; never touches `Equity`.
- **`ArmedEntry`** resting-order aggregate (`ArmedEntryStatus { Armed, Triggered }`; id == the future trade id; carries
  the Setup + arm-time `Size` + `RiskBudget`; raises `EntryArmed`/`EntryTriggered`).
- **`PaperTradeFactory.Arm`** (sizes ONCE at arm time + reserves; **atomic** — builds the `ArmedEntry` BEFORE reserving,
  mirroring `Open`+`RegisterOpen`) + **`OpenArmed`** (opens the trade under the reservation's Guid and does **NOT** call
  `RegisterOpen` — the trigger is a **key re-label**, so the existing `Settle` releases it byte-unchanged; guards the
  once-only trigger before constructing). **The reserved `Money` == the trade's derived `RiskBudget` by construction**
  (`PriceToPips ≡ /PipSize`), byte-exact over 36k adversarial handoffs.
- **Deferred (spec §5 item 32 / cut 2b):** `PaperAccount.Release` + the no-chase cancellation precedence (so a stale arm
  that never triggers doesn't leak cap), the `EntryManager` orchestrator + `EntryContext`/`EntryPlan`/`EntryAction`, the
  same-bar entry-then-stop **−1R straddle** (re-feed the exit `FillEvaluator` — the one worst-case authority), and
  `EntryMode { Armed (default), Immediate }`. The read-side `OpenRisk` decimal-associativity tail (~1E-23, proven
  cosmetic, far below cent) → round at a future reporting boundary.

**WP5 entry-arming cut 2b-i — `EntryManager` fill + same-bar straddle (issue #39, branch `feature/#39-entry-manager-fill`,
PR #40) — DONE.** The pure DECIDE half of the §2.5.1-step-7 entry orchestration, mirroring `ExitManager`/`ExitPlan`/
`ExitAction`/`ExitContext`. ict-domain-expert CONFORMANT (0 Critical/Should-fix), `defensive-guardrail-auditor` 7/7,
`pr-reviewer` APPROVE; an **adversarial driver drove 640k cases (all 7 invariants HOLD, 0 violations, two negative
controls fired)**. **392 tests** (369 unit + 23 arch), 0 warnings, format clean:
- **`EntryManager.Decide(ArmedEntry, Candle, EntryContext) → EntryPlan`** (pure, clock-free). Delegates the limit touch
  to `EntryFillEvaluator`; a no-touch bar is **`NoOp`** (the limit keeps resting — don't chase). On `Filled` → an
  `EntryAction.Open` at the bar-close, then the **same-bar straddle** is resolved by re-feeding the SAME candle to the
  exit `FillEvaluator` (the ONE StopFirst worst-case authority) on a **would-be transient trade** (events cleared): a
  same-bar STOP → apply-ordered **`[Open, Close]` = −1R**; a same-bar RUNNER is **NOT credited** (only `Open`, left for
  the steady-state exit pass — the conservative no-free-win asymmetry). Opening at the **bar-close** time is load-bearing
  (the equal-timestamp open→close passes `GuardActivityTime`). The straddle `Close` books the costed full round trip
  (`Compute` = entry crossing + exit + commission); a clean open books no cost (entry spread rides the deferred
  exit-leg line). `EntryContext`/`EntryAction`/`EntryPlan` mirror the exit-side VOs.
- **`ArmedEntry` now carries its money geometry** (`PipSize`/`ValuePerPip`, set at `Arm`) so the orchestrator can build
  the would-be trade and **`OpenArmed` opens at the identical geometry** — and `OpenArmed` no longer takes separate
  `SymbolSpec`/`ContractSpec` (removes a drift class where the open specs could disagree with the arm-time sizing).
- **Deferred (spec §5 item 33 / cut 2b-ii):** the **no-chase cancellation** precedence (killzone-end / no-overnight /
  max-wait) + `PaperAccount.Release` + `ArmedEntry.Cancel`/`Cancelled` + `EntryCancelReason`; `EntryMode { Armed,
  Immediate }`; and the **module orchestrator that wires entry→exit** — it MUST re-feed the SAME bar to the exit pass
  after `OpenArmed` so a same-bar runner that genuinely traded isn't missed (reviewer-flagged); plus promptly settling a
  straddle-closed trade so the cap isn't transiently over-counted.

**WP5 entry-arming cut 2b-ii — `EntryManager` no-chase cancellation (issue #41, branch
`feature/#41-entry-no-chase-cancellation`, PR #42) — DONE.** Completes the entry orchestrator: the no-chase
cancellation precedence in `Decide`, run BEFORE the fill. ict-domain-expert CONFORMANT (0 Critical/Should-fix),
`defensive-guardrail-auditor` 7/7, `pr-reviewer` APPROVE. **405 tests** (382 unit + 23 arch), 0 warnings, format clean:
- **`killzone-end > max-wait`** — killzone-end = `!KillzoneClock.IsActiveEntry(candle.OpenTimeUtc, instrumentClass, ActiveKillzones)`
  (the bar left the active killzone — window over / lunch / index cutoff), reusing the same §4.6 hunt-set the entry
  detector uses (arm + entry windows can't drift); max-wait = the INVENTED, provenance-flagged backstop (default 240,
  generous so killzone-end normally fires first). Each emits an `EntryAction.Cancel` the caller applies as
  `ArmedEntry.Cancel` + **`PaperAccount.Release`** (symmetric ledger removal — the cap **self-heals**, no leak).
  Cancellation **outranks a would-be fill** (don't chase).
- **No-overnight is NOT a separate rung:** no FX active killzone spans 00:00 NY (Asian ends at 00:00), so any midnight
  cross already trips killzone-end — redundant for a PENDING limit (unlike the exit time-exit, load-bearing for a HELD
  trade). Both reviewers verified the reasoning.
- New: `EntryCancelReason`, `EntryAction.Cancel` (+ `CancelReason`), `ArmedEntry.Cancel`/`Cancelled` + `InstrumentClass`,
  `PaperAccount.Release`, the `EntryCancelled` event, `EntryManagementOptions` (`Ict:Execution:Entry`) + `EntryMode
  { Armed (default), Immediate }`. `EntryManager` now injects `KillzoneClock` + `KillzoneEntryOptions` + the options.
- **Killzone classification AXIS — RESOLVED in the review pass:** cancellation now classifies from `candle.OpenTimeUtc`,
  the SAME axis `MarketContext`/`KillzoneEntryDetector` use (no boundary-straddle drift). **Deferred (spec §5 item 34,
  both reviewers non-blocking):** a structural guard that no selectable killzone spans 00:00 NY (makes the no-overnight
  drop provably safe vs a future config); an index-class cancellation test; Cancel+Release atomicity at the module
  applier; the **entry→exit same-bar re-feed** (a same-bar runner re-fed to the exit pass after `OpenArmed`).

**ICT-domain-completion audit (workflow `wf_25901e98-7f3`) — the backlog driving the current sequence.** A 5-agent
audit mapped the remaining ICT domain into a prioritized, transcript-cited 14-item backlog (detectors §2.5.6, long-tail
§2.5.8, money-management §2.4/§2.5.5, grading §2.5.3/§2.5.4, spec §5 items). The order-1/2 foundational items (settle
contested decisions + grading) and the independent money slice (adaptive risk) were built first; the detector/target
corrections follow. Backlog ids (EG-/FVG-/OB-/TIME-/TGR-) are the cited keys.

**WP5 module orchestrator — `TradeOrchestrator` (issue #43, PR #45 → merged) — DONE.** The pure per-candle process
(`IctTrader.Domain/Trading/`: `TradeOrchestrator`/`ManagedPosition`/`ITradeOrchestrator`) composing entry→exit into a
runnable cycle. `OnSetupConfirmed` arms (`EntryMode.Armed` default) or opens (Immediate); `Advance` applies the
`EntryManager` plan (open / same-bar −1R straddle / no-chase cancel + Release), **re-feeds the SAME bar to the
`ExitManager` after a clean open** so a same-bar runner/T1/trail isn't missed, applies the exit plan, and **settles
every terminal close promptly** (the ~5% cap is never transiently over-counted). ict-domain-expert CONFORMANT,
guardrail 7/7, pr-reviewer APPROVE. CodeRabbit resolved.

**WP2 persistence — PaperTrading EF Core (issue #44, PR #46 → merged) — DONE.** EF Core write-model persistence for
`PaperTrade`/`PaperAccount`/`ArmedEntry` (`Modules/PaperTrading/Infrastructure/Persistence/`): per-aggregate
`IEntityTypeConfiguration` (backing-field access, `xmin` concurrency, JSONB for the append-only fill-leg + reservation
ledgers + the `TradePlan`/`Setup` snapshots, plan §7 numeric/timestamptz), `PaperTradingDbContext` + a design-time
factory (env-var → appsettings, **fail-fast, no committed credentials**), an `InitialCreate` migration with FKs to
`paper_accounts`, and 5 Testcontainers round-trip tests. Invariant-safe private parameterless EF ctors on the three
aggregates. guardrail 7/7, pr-reviewer APPROVE; CodeRabbit (armed-entry FK, factory fallback, Docker probe) resolved.

**WP4 adaptive `IRiskManager` — loss-ladder + win-cycle (issue #47, PR #48 → merged) — DONE.** Pulls sizing off flat
base risk to the §2.4/§2.5.5 model. Pure `RiskManager.EffectiveRisk(RiskState, RiskOptions)`: win-cycle **milestone**
override (every Nth consecutive win → lowest unit, a cycle not a latch) → base/restore → loss-ladder by consecutive
losses (1% → 0.5% → 0.25%, Mentorship-verbatim). `PaperAccount` tracks the streaks + equity peak/drawdown-trough at
`Settle`, **classified by the GROSS structural outcome** (a cost-only scratch is a breakeven, not a loss), and exposes
a persisted `RiskState` (4 columns). `PaperTradeFactory` sizes Open/Arm from `EffectiveRisk` (arm-time effective-%
**frozen**, so reserve == RiskBudget holds). **Restore is RECOVERY-GATED** — base returns only on a ≥50% dip recovery
or a new equity high, never a single win (decisions register **TGR-5**; win-gated is a deferred non-default
`RestoreMode`). `HardMaxRiskPercent` is capped at the §2.5.5 4.5% ceiling. ict-domain-expert SHIP (caught + drove the
fix of a win-cycle **latch** bug), guardrail 7/7, pr-reviewer APPROVE, **money-math adversarial driver 680k cases / 0
violations** (negative-control validated). CodeRabbit (hard-max ceiling, gross-vs-net) resolved.

**Core-model decisions register + TGR-4 grading (issue #49, PR #50 → merged) — DONE.** The audit's foundational
orders 1–2. The contested §2.5 decisions were resolved transcript-cited (`ict-core-model-decisions` workflow
`wf_de67d483-8be`) into **`docs/ict-core-model-decisions.md`** — the cited SoT every detector/target slice now
references by id (EG-1 OTE 0.62–0.79 + 70.5%-is-Primer-only; EG-2 the two-frame PD anchor keeping Σ=9.75; FVG
semantics; OB-9a cluster-start-open; TIME-11-12 multi-candle MSS; TIME-10 08:30 reference; TGR-1/2 SD targets). **TGR-4
grading**: `SetupScorer.GradeFor` now auto-clears an all-RequiredConditions setup to ≥**B** (A only at
`GradeAThreshold`); the bare-required score (6.15/9.75 = **63**) is pinned by a regression test, and the 0–100 score
becomes the within-grade sorter — retiring the C-suppression that made the canonical §2.5 model un-alertable.
**Unblocks the Alerting WP.** Grade A is unreachable (~77) until the optional emitters ship. ict-domain-expert
CONFORMANT, guardrail 7/7, pr-reviewer APPROVE.

**Register-correction slices (the §2.5 fidelity backlog) — 11 MERGED (COMPLETE).** Built strict-gated (ict-domain-expert,
guardrail, pr-reviewer; meatier slices via a design judge-panel + a 4-lens adversarial-verify workflow). Unit tests **431 → 560**:

- **OB-9a** (issue #53, PR #54) — order block = consecutive opposite-close **cluster**, anchored at the run-start
  candle's open; `OrderBlock` body-based mean-threshold (`BodyLow`/`BodyHigh`); `OrderBlockOptions.MaxClusterCandles`=3.
- **FVG-SEM-1a** (issue #55, PR #56) — `FvgOptions.TouchSemantics {WickInto(default), CloseInto}`; formation = touch 0.
- **EG-1** (issue #57, PR #58) — the displacement leg was **wick-anchored** (a real fidelity gap); now **body-to-body**
  by default (`DisplacementOptions.AnchorMode {BodyToBody,WickToWick}` + `WickAnchorOnFomcNfp`, NY-date-keyed, fail-open
  to body). The OTE entry AND `Displacement.EquilibriumPrice` move together; the daily-range PD veto is untouched.
- **TIME-11-12** (issue #59, PR #60) — displacement is a multi-candle **leg** (`DisplacementDetector` grows a backward
  strictly-monotonic run, hard-capped at `DisplacementLegMaxBars`=3, net-thrust energy); MSS confirms on the **earliest**
  leg member that closes beyond the swing (member-scan, `FormedAtUtc` guard, sweep-strict-precede to the breaking member).
  `Displacement` gained `OriginAtUtc`/`LegBars` via a ctor overload (single-candle path byte-identical).
- **TIME-10** (issue #61, PR #62) — `MarketContext.MacroOpen` (08:30 NY, DST-aware, per-day reset) + `ReferenceOpen(premium)`
  (FX-default midnight; else `min`/`max` of {midnight, macro} bearish/bullish — Ep17 dual-reference); behind
  `MarketContextOptions.UseMacroOpenReference` (default false → FX byte-identical). `LiquiditySweepDetector.IsJudas` reads it.
- **FVG-SEM-2a** (issue #63, PR #64) — `FvgOptions.StrictFirstFvg` (default off) selects the **shallowest** in-band gap
  (Ep3 "first higher fvg") over the same FVG+OB set; activates the `FairValueGap.IsSelectedEntry`/`Stacked` markers
  (resolver pure, `OteFibDetector` single writer); stacked **detection** carries the farther-gap far edge for 2b
  (CodeRabbit fix: the farther-gap scan uses the broader open set, not the band-filtered subset).
- **FVG-SEM-3** (issue #65, PR #66) — the five validity exclusions (no-sweep / Asian-range / counter-bias / no-CHoCH /
  overlapping-wicks) as **flag-only evidence** (6 `EvidenceKeys`, proven scoring-inert; `FvgOptions.ApplyValidityExclusions`
  default off → byte-identical). When on it vetoes ONLY Asian-range + overlapping-wicks (the other three are already FSM
  RequiredConditions). Asian-range classifies from the gap's **formation** candle; a vetoed FVG still carries the diagnostic
  (`DetectorResult.NoMatchWith`). Asian killzone is already selectable (locked) — "deprioritized" = off-by-default, NOT a weight.
- **TGR-1/2 Slice A** (issue #67, PR #68) — SD-projection geometry on a single shared leg axis: `Displacement.Project(f)`
  (= `Terminus + f·(Origin−Terminus)`) is now THE axis (`OteEntryResolver.Retrace` delegates to it, byte-identical), and the
  pure **NON-scoring** `SdProjectionResolver` prices −1/−1.5/−2 SD via `leg.Project(−n)` reading only `ctx.LastDisplacement`
  (TGR-2 single-source — SD & OTE provably can't drift). `SdProjectionOptions` (default Enabled=false; negative-fib a
  Primer-flagged opt-in). Σ=9.75 untouched.
- **TGR-1/2 Slice A.2** (issue #69, PR #70) — `TargetLadder` is now **N-tier** with an explicit `RunnerIndex` (the RR tier =
  the gated draw); `DrawOnLiquidityDetector` emits the SD tiers as additive evidence (gated, default off), `PricedFrame`
  carries them, `SetupFactory` appends those **strictly beyond T2** as deeper advisory targets. Enabling SD never inflates
  the gated RR (runner pinned to the draw). Default path byte-identical; the 2-arg ladder ctor kept.
- **FVG-SEM-2b + EG-3** (issue #72, PR #73) — the entry-orchestrator chain. **Stacked stop-sizing**:
  `DrawOnLiquidityDetector` widens the stop to clear the farther gap (`min(sweep−buffer, fartherBound−buffer)`), computed
  before the RR floor (a stacked setup below the floor is a faithful NoMatch), gated on `StrictFirstFvg`. **Wrong-order
  nix**: an `EntryManager.ResolveCancellation` rung (precedence killzone-end > max-wait > nix > fill, pre-fill, `IsStacked`-
  gated) via `EntryCancelReason.StackedFartherGapHitFirst`; the farther bound threads `StackedFartherBound` evidence →
  `PricedFrame` → `Setup` (not `TradePlan`) → `ArmedEntry` (+ an EF migration column). **EG-3 v1**: `EntryFillEvaluator`
  records the touched price clamped within `CloseProximityTolerancePips` (`UseCloseProximityEntry`, default off) but
  `OpenArmed` still opens at `Plan.Entry` — the frozen-1R invariant holds (a stop-out books exactly −1R). `EntryFillEvaluator`
  now REQUIRES a `SymbolSpec` (CodeRabbit — no FX-major default).

**🏁 The §2.5 model is COMPLETE.** The audit's entire fidelity backlog (OB-9a · FVG-SEM-1a/2a/2b/3 · EG-1/EG-3 ·
TIME-10/11-12 · TGR-1/2 A+A.2) is merged. The canonical *ICT 2022 Intraday FVG Model* — sweep → MSS/displacement →
PD-array OTE entry → SD/draw targets, with the entry-arming, stacked-gap, and management chain — is faithfully encoded
end-to-end as a pure, deterministic, ICT-gated domain. **The next phase is making it RUNNABLE (WP7).**

**WP7 slice 1 — Host Options binding + `ValidateOnStart` (issue #75, PR #76 → merged) — DONE.** The first runnable-
backend cut: the Host now binds **all 24 `Ict:*` Options POCOs** to their config sections and self-validates each at
startup, so a mis-configured host fails fast with the section-qualified reason instead of silently mis-running the model.

- **`IctOptionsRegistration.AddIctOptions(IServiceCollection, IConfiguration)`** (`src/IctTrader.Host/`) binds every POCO
  (Confluence/scanning ×4 · Detection ×14 · Risk+execution ×6) via a private generic helper —
  `AddOptions<T>().Bind(section).ValidateOnStart()` + a singleton `IctOptionsValidator<T>` (internal) that delegates to the
  POCO's own `Validate()` and returns `ValidateOptionsResult.Fail($"{section}: {e}")`. `Program.cs` calls it after the
  DefensiveOptions block; **DefensiveOptions is deliberately NOT in the set** (it keeps its own bespoke validator).
- **New `tests/IctTrader.IntegrationTests` project** (references the Host) — `HostOptionsValidationTests` (3): default
  config binds clean (asserts the verified defaults across Risk/Fvg/Displacement/EntryManagement/SdProjection/Confluence);
  `Ict:Risk:BaseRiskPercent=0` throws `OptionsValidationException` matching `*Ict:Risk*`; `Ict:Displacement:DisplacementLegMaxBars=0`
  throws matching `*Ict:Displacement*`. **560 unit + 23 arch + 3 integration**, 0 warnings, format clean. CodeRabbit's lone
  Major (claimed missing global usings → compile failure) was a verified **false positive** — the csproj declares
  `<Using Include="Xunit"/>` + `<Using Include="FluentAssertions"/>`, tests pass green; skipped with reason (adding
  redundant file usings would trip IDE0005 under warnings-as-errors). guardrail 7/7, pr-reviewer APPROVE.

**WP7 slice 2a — the in-memory message bus (issue #78, branch `feature/#78-in-memory-message-bus`, PR #79) — DONE.**
The modular monolith's only inter-module seam (plan §3.0a) finally has an implementation — the keystone the
whole scan loop rides on. A 5-reader **understand workflow** (`wf_7471efe4-8a1`) first mapped the messaging/DI
seam, the frozen contracts, and the scanning + paper-trading composition recipes (read from the integration
tests). pr-reviewer APPROVE (3 nits applied), guardrail 7/7, **569 unit (+9) + 23 arch**, 0 warnings, format clean:

- **`InMemoryMessageBus : IMessageBus`** (`src/SharedKernel/Messaging/`) — a stateless singleton that opens a
  **fresh DI scope per dispatch** via `IServiceScopeFactory` (clean per-dispatch unit-of-work, no captive
  dependency). `SendAsync`/`QueryAsync` route to **exactly one** handler (fail-fast on 0 or >1); `PublishAsync`
  fans out to **0..N** handlers awaited **sequentially in registration order** — NO Channels/concurrent fan-out
  (the scan path is deterministic + order-dependent: a stop-out must settle before the next bar). A published
  event's handlers share the one dispatch scope. `QueryAsync` reconstructs the closed `IQueryHandler<,>` from
  the query's **concrete runtime type** (the param is `IQuery<TResult>`) and invokes via reflection; handler
  exceptions propagate (fail-fast).
- **`MessagingRegistration.AddMessaging(params Assembly[])`** — `TryAddSingleton` the bus + **Scrutor**-scans the
  given module `*.Application` assemblies for the three handler interfaces (Scoped lifetime), registering each
  **only under its matched closed handler interface** (predicate-filtered `AsImplementedInterfaces`, so an
  incidental `IDisposable`/second role can't pollute resolution).
- **Packages:** `Scrutor 7.0.0` + `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.9` added to
  `SharedKernel` — **arch-test-safe** (neither internal `IctTrader.*` nor on the
  MediatR/FluentAssertions/Moq/SpecFlow denylist; 23 arch tests still green). Host `Program.cs` calls
  `AddMessaging()` (bus-only for now).
- **Deferred (2c+):** passing the module `*.Application` assemblies to `AddMessaging` lands with the first real
  handlers; cross-cutting **decorators** (logging/validation/idempotency) deferred. **Roadmap:** 2b
  `ReplayMarketDataFeed` → `CandleIngested`; 2c Scanning handler + per-symbol scanner registry → `SetupConfirmed`
  (**settle the scan→trade seam** — `SetupConfirmed` carries a lossy `SetupDto`, no money geometry); 2d
  PaperTrading handler + the (not-yet-existing) aggregate repositories + `TradeOrchestrator` drive; 2e Host
  `ScannerHostedService` + SignalR push + bus-backed REST.

**WP7 slice 2b — read-only Replay feed + ingestion (issue #80, branch `feature/#80-replay-feed-ingestion`,
PR #81) — DONE.** The MarketData "left half" of the scan loop — the candle SOURCE (plan §4.1/§6.1/§6.3).
pr-reviewer APPROVE (no Critical/Should-fix), guardrail 7/7, **579 unit (+10) + 23 arch**, 0 warnings, format clean:

- **`IMarketDataFeed`** (`MarketData/Application/Abstractions/`) — a **read-only-by-SHAPE** candle source:
  `Provider` + `IAsyncEnumerable<CandleDto> StreamCandlesAsync(ct)`, and **no write/order method** — so a feed
  is structurally read-only, not flag-gated (CodeRabbit-hardened: the impl-varying `IsReadOnly` bool was
  removed — a bool an impl controls is "flag-disabled", the opposite of the §6.3 philosophy; the read-only
  *status* is reported on the frozen `FeedStatusDto.IsReadOnly`).
- **`MarketDataIngestor`** (`MarketData/Application/Ingestion/`) — `IngestAsync(ct)` `await foreach`es the feed
  and publishes one `CandleIngested(CandleDto)` per candle on the `IMessageBus`, in candle order (the feed has
  no write path, so no runtime read-only check is needed).
- **`ReplayMarketDataFeed`** (`MarketData/Infrastructure/Feeds/`) — `IsReadOnly => true`, `Provider => "Replay"`;
  ctor **stable-sorts the supplied candles by `OpenTimeUtc`** so chronological delivery is structural (a replay
  reproduces a live run bit-for-bit); cancellation-honoring async iterator.
- **`CsvCandleSource`** (`MarketData/Infrastructure/Feeds/`) — `Parse(TextReader)`/`Load(path)`: header-skipping,
  blank-line-ignoring, **invariant-culture** CSV (`Symbol,Timeframe,OpenTimeUtc,O,H,L,C,V`); `OpenTimeUtc` read
  as **UTC** (`AssumeUniversal|AdjustToUniversal`, offsets normalised); a malformed row throws `FormatException`
  **with its line number**. `UnitTests` now references MarketData.Application+Infrastructure (arch governs
  production only); 10 tests (3 ingestion incl. a real-bus capture, 7 CSV incl. header-shape validation).
- **Deferred (2e):** the hosted `BackgroundService` driving `IngestAsync` + Host DI/fixture-path wiring
  (`ReplayFeedOptions`, `Ict:MarketData:Replay`); the resilient-feed decorator + OANDA/Finnhub/TraderMade/MT5
  read-only adapters (§6.1) + a `ReadOnlyFeedGuard` decorator; the `TickIngested` path. **No subscriber yet —
  2c (Scanning) consumes `CandleIngested`.**

**WP7 slice 2c — the Scanning scan loop (issue #82, branch `feature/#82-scanning-scan-loop`, PR #83) — DONE.**
The HEART of the loop: `CandleIngested` → a per-(symbol,style) `SymbolScanner` (the pure-domain `MarketContext` +
the 14-detector **pinned** pipeline + the `ScanSession` FSM + `SetupFactory`) → a confirmed advisory `Setup` →
`SetupConfirmed(SetupDto)`. Built by the `vsa-slice-builder` agent from the understand-workflow recipe, then
strict-gated (ict-domain-expert **CONFORMANT 4/4**, guardrail 7/7, pr-reviewer APPROVE — its one Should-fix
applied). **No Domain/Contracts changes.** 581 unit (+2) + 23 arch, 0 warnings, format clean:

- **`SymbolScanner`** (`Scanning/Application/Scanning/`) — stateful per (symbol,style); builds the exact
  `ScanSessionTests` recipe (SwingPoint→…→MSS pinned; shared `Fvg`/`Ote`/`PremiumDiscount`/`Liquidity`/`TradeStyle`
  Options instances so the §2.5 chain can't drift); `Setup? OnCandle(Candle)`. Single-symbol mutable state → one
  instance per symbol, chronological feed. A documented **test-only `prependDetectors` seam** (production passes
  null → byte-identical) lets the test seed structural state the way `ScanSessionTests` does.
- **`SymbolScannerFactory`/`Registry`** — singleton get-or-create per (symbol,style) (`ConcurrentDictionary`);
  `ScannerOptions` snapshots the 18 validated `Ict:*` POCOs once. **`CandleIngestedHandler`** (`IEventHandler`,
  bus-scoped) maps `CandleDto`→domain `Candle`, scans each `MarketContextOptions.ActiveStyles` (= `Ict:Scanning`,
  default `[Intraday]`), and publishes `SetupConfirmed`. `AddScanningModule` registers the factory+registry
  singletons (the handler is `AddMessaging`-scanned).
- **Scan→trade seam — DECIDED (Architecture A):** the bus carries the lossy-but-sufficient `SetupDto`; the
  PaperTrading consumer rebuilds a domain Setup from it in 2d. **Canonical wire target ordering:** `Targets[0]`=T1
  partial (entry→runner equilibrium, §2.5.5), `[1]`=runner (the gated-draw RR tier), `[2..]`=advisory SD —
  exactly `TradePlan.TargetLadder.Targets`. `SetupDtoMapper` projects entry/stop/targets/RR straight off the plan
  (no recompute → frozen-1R/RR preserved); enum fields carry the **member names**; `Killzone` from
  `scanner.CurrentKillzone` (the confirming candle's session — `Setup` carries none).
- **`SetupDto.Id` is DETERMINISTIC** (SHA-256 of symbol|style|tf|direction|entry|stop|detectedAt → GUID) — the
  pr-reviewer Should-fix: a fresh `Guid.NewGuid()` would let a replayed/redelivered candle emit a different id
  for the same setup → a duplicate paper trade once 2d subscribes; a deterministic id is a free idempotency key
  (the whole DTO now replays byte-identically; the determinism test asserts it incl. the id).
- **Deferred:** **2d** = the PaperTrading `SetupConfirmedHandler` rebuilds a domain `Setup` from `SetupDto` (needs
  a domain `Setup`/`TradePlan` **rehydrate-from-priced** factory) + per-symbol `SymbolSpec`/`ContractSpec` + the
  aggregate **repositories** (none exist) + `TradeOrchestrator` drive + settle. **2e** = the Host
  `ScannerHostedService` (DI, SignalR, REST). Flagged for later: `SymbolScanner` hardcodes `SymbolSpec.FxMajor`
  (**FX-only** classification
  — a per-instrument `SymbolSpec` lookup must precede any index symbol or the §2.5.7 index killzone never applies);
  a real organic multi-bar fixture (vs the seeded seam) would close the last detector-pipeline coverage gap.

**WP7 slice 2d-i — `SetupRehydrator` (issue #84, branch `feature/#84-setup-rehydrator`, PR #85) — DONE.** The
CONSUMER half of the scan→trade seam (Architecture A): `SetupRehydrator` (PaperTrading.Application, internal)
rebuilds a domain `Setup` from the wire `SetupDto` so PaperTrading can later size/open a trade. ict-domain-expert
**CONFORMANT 4/4** ("ship"), guardrail 7/7, pr-reviewer APPROVE (its one Should-fix applied). 589 unit (+8) + 23
arch, 0 warnings, format clean:

- **Geometry round-trips byte-identical** — `Price`/`RewardRatio` store decimals verbatim (no quantization) and
  the `TradePlan`/`TargetLadder` ctors **recompute RR from entry→runner**, so the rebuilt RR equals the scanned
  RR and the frozen 1R (=|entry−stop|, §5.2) is bit-for-bit; a deliberately-wrong wire RR is **ignored** (test).
- **Two documented wire losses, both safe for the default path:** the exact within-grade **score** is not on the
  wire → rebuilt as the grade's configured FLOOR (`GradeAThreshold` 80 / `GradeBThreshold` 65; grade-consistent,
  unused by the trade path which reads only Plan/Symbol/Style/Timeframe); **`StackedFartherBound`** is not a
  `SetupDto` field → null (only material under the non-default `StrictFirstFvg` — flagged: carry it on the
  contract before enabling that live). `DetectedAtUtc` normalised to UTC; a bad enum member fails fast.
- **S1 hardening (both reviewers):** the runner-tier index was a bare literal `1` in three places
  (`SetupFactory`, the `TargetLadder` legacy ctor, the rehydrator) with no wire field — a future producer change
  could silently misplace the RR tier. Hoisted to a single shared **`TargetLadder.CanonicalRunnerIndex`** const
  all three now reference, + a 3-tier (SD) rehydrator test pinning the runner to index 1 (the gated draw, not the
  deepest SD tier) so enabling SD can't inflate the rebuilt RR.
- **New pattern:** `[InternalsVisibleTo("IctTrader.UnitTests")]` on PaperTrading.Application (first use) — tests
  the internal rehydrator directly while the module's public surface stays Contracts-only.
- **Deferred (2d-ii/iii):** the PaperTrading `SetupConfirmedHandler` (consumes the rehydrator) + per-symbol
  `SymbolSpec`/`ContractSpec` + the aggregate **repositories** (none exist) + `TradeOrchestrator` `OnSetupConfirmed`/
  per-candle `Advance` + settle + `AddPaperTradingModule`. A `SetupDto` carrying score / `StackedFartherBound` /
  an explicit runner index would be a (currently-unneeded) frozen-contract change.

**Process cadence (per the operator):** keep the ICT gate strict (`ict-domain-expert` + guardrail + `pr-reviewer`,
concurrent) but move faster — build directly from the locked design (skip the separate pre-spec when pinned), ship
bigger complete slices, and reserve the heavy ~600k-case adversarial driver for numeric/money-math slices (it fuzzes
numeric correctness, not ICT fidelity). Under Ultracode: settle subtle ICT calls with a single ict-domain-expert spec
(or a design judge-panel for wide design spaces), implement via `ict-detector-engineer`, then adversarially verify.

**Still to come — the §2.5 fidelity backlog is COMPLETE (all 11 register slices merged).** The NEXT PHASE is the runnable
backend (WP7). Optional domain follow-ons that remain: **TGR-1/2 Slice B** (SD-as-primary/fallback draw,
`AllowSdAsPrimaryDraw` — touches `DrawOnLiquidityDetector` + the RR gate, fires only when no untapped opposite pool
qualifies) and the §2.5.8 long-tail (SMT/Breaker, session macros, weekly bias, HRLR, Power-3, Sunday-gap) — all additive.

The runnable backend: **WP7 host wiring** — Options binding **DONE (slice 1, PR #76)**: all 24 `Ict:*` POCOs bound +
`ValidateOnStart`-gated via `IctOptionsRegistration.AddIctOptions`. Still to wire: the deferred
`WindowCapacity ≥ DisplacementLegMaxBars` cross-check, the `Ict:Detection:Fvg` binding INTO the OTE/draw detectors, and the
per-instrument `SymbolSpec` injected into `EntryFillEvaluator` for EG-3;
**slice 2 (the scan loop)** = DI the `PaperTradingDbContext` + aggregate-scoped repositories + `TradeOrchestrator`;
a Replay feed → the Scanning/PaperTrading bus handlers → SignalR + REST) and the **Alerting** module (unblocked by TGR-4);
the **`Performance` calculator (WP6)**. Optional long-tail (§2.5.8, additive): **SMT/Breaker** detectors, session macros,
weekly bias, HRLR/`NeutralCondition`, Power-of-Three/AMD, Sunday-gap; the **slippage**/**session-stepped spread**/**swap**
cost follow-ons; lot-step **flooring** of the partial leg. **WP8 frontend scaffold — MERGED (PR #34, issue #30).**
