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

**WP7 slice 2d-ii — PaperTrading aggregate repositories + unit-of-work (issue #86, branch
`feature/#86-papertrading-repositories`, PR #87) — DONE.** The persistence the trade handler needs (plan §3.0/§7).
Built by the `ef-persistence-engineer` agent; I corrected its one architectural deviation (see below). pr-reviewer
APPROVE (correction verified complete), guardrail 7/7, **590 unit + 23 arch + 15 integration** (Testcontainers, 1
skipped placeholder), 0 warnings, format clean:

- **Domain interfaces** (`IctTrader.Domain/Repositories/`, aggregate-scoped, NO generic repo, EF-agnostic):
  `IPaperAccountRepository` (`GetByIdAsync`/`AddAsync`), `IPaperTradeRepository`
  (`GetByIdAsync`/`AddAsync`/`GetOpenAsync`), `IArmedEntryRepository` (`GetByIdAsync`/`AddAsync`/`GetActiveAsync`),
  `IPaperTradingUnitOfWork` (`SaveChangesAsync`). Return domain aggregates + BCL only → Domain stays
  dependency-free (arch green).
- **EF impls** (`PaperTrading/Infrastructure/Persistence/Repositories/`, `internal sealed`) wrap the existing WP2
  `PaperTradingDbContext`: `GetOpen`/`GetActive` filter the enum-string status column → **SQL `WHERE`** (no
  client-eval); the repos + UoW share the scoped DbContext so `SaveChangesAsync` is **one `xmin`-guarded atomic
  commit** per bus dispatch; JSONB ledgers / risk-state / `Setup` snapshots rehydrate via the private EF ctors.
- **`AddPaperTradingPersistence`** DI extension registers the repos + UoW **Scoped** (the DbContext itself is the
  Host's job — 2e). `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.9` + `InternalsVisibleTo
  IctTrader.IntegrationTests` on Infrastructure. 6 Testcontainers round-trips (add+reload each aggregate incl.
  JSONB ledgers; open→**settle** mutate-and-save survives reload; `GetOpen`/`GetActive` subset filters).
- **Architecture correction (mine):** the agent first placed the interfaces in `PaperTrading.Application` on the
  flawed premise that the Domain "can't" hold repo interfaces returning aggregates — but a repo interface in the
  Domain returning a Domain aggregate adds NO external ref, and plan §3.0/§3.1 explicitly mandates Domain. Moved
  all four to `IctTrader.Domain/Repositories/` + made the UoW doc EF-agnostic (it `<see cref>`'d an EF exception
  that won't resolve in the EF-free Domain). Renamed `GetAsync`→`GetByIdAsync` for a uniform surface (pr-reviewer nit).
- **Deferred (2d-iii):** the PaperTrading `SetupConfirmedHandler` (`SetupConfirmed` → `SetupRehydrator` →
  load/create `PaperAccount` → `TradeOrchestrator.OnSetupConfirmed` → persist via the UoW → publish trade events),
  the `CandleIngested` per-candle `Advance`, a `ManagedPosition` registry, `AddPaperTradingModule`, and the
  `TradeOrchestrator` object-graph wiring. **2e** = Host DI of the DbContext, `AddPaperTradingPersistence`, the
  connection string, and the hosted scanner.

**WP7 slice 2d-iii — PaperTrading orchestration: open + manage trades (issue #88, branch
`feature/#88-papertrading-orchestration`, PR #89) — DONE.** 🎯 The scan loop is now **functionally complete
end-to-end in-process**: `SetupConfirmed` → a trade opens/arms → `CandleIngested` advances it → it settles, all on
the bus with persistence + events. Built by the `vsa-slice-builder` agent; gated **ict-domain-expert CONFORMANT
5/5**, **guardrail 7/7**, **pr-reviewer APPROVE**. 593 unit (+3 flow incl. a loss-path) + 23 arch + 15 integration,
0 warnings, format clean:

- **`SetupConfirmedHandler`** (`IEventHandler<SetupConfirmed>`) — `SetupRehydrator` → load/create the demo
  `PaperAccount` → resolve `SymbolSpec`/`ContractSpec` → `TradeOrchestrator.OnSetupConfirmed` (arm default / open
  Immediate) → persist via the UoW → publish `PaperTradeOpened` (Immediate) or nothing yet (Armed rests).
- **`PaperTradingCandleHandler`** (`IEventHandler<CandleIngested>`) — **DB-AS-STATE** (the key design): loads the
  symbol's active armed entries (`GetActiveAsync`) + open trades (`GetOpenAsync`) FRESH each candle (scope-safe,
  restart-safe — no detached-entity cache), reconstructs `ManagedPosition.Resting`/`.Live`, `Advance`s (which
  **settles** terminal closes), persists, and publishes `PaperTradeOpened` (arm-trigger) / `PaperTradeClosed`.
- **`TradeOrchestratorFactory` + registry** (per-symbol — `EntryFillEvaluator` binds `SymbolSpec`; mirrors 2c)
  build the exact `TradeOrchestratorTests` object graph. **`PaperAccountProvider`** load-or-creates ONE demo
  account (fixed Guid; `PaperTradingOptions.StartingEquity` = `Ict:PaperTrading`, default 10 000; cap reused from
  `RiskOptions.MaxOpenPortfolioRiskPercent` — one cap owner). `AddPaperTradingModule` registers the
  factory/registry/provider/options; the handlers are `AddMessaging`-scanned; the DbContext +
  `AddPaperTradingPersistence` are the **Host's** job (calling them here = a circular Application→Infrastructure ref).
- **Bus dispatch note:** `IMessageBus.PublishAsync<TEvent>` binds handlers off the **compile-time** `TEvent`, so the
  handlers publish the **concrete** `Contracts.PaperTradeOpened/Closed` (an `IEvent` ref would resolve 0 handlers),
  and `ClearDomainEvents()` after draining so a still-open trade carried under DB-as-state can't re-publish.
- **Deferred Should-fixes (reviewer-flagged, all non-blocking — TRACKED):** **(before WP6/Alerting)** `SetupId` =
  `trade.Id` and `Killzone` = null are **placeholders** — the `PaperTrade` aggregate doesn't retain the source setup
  id / killzone, so emitting the truth on every event (incl. a prior-candle **close**, no `Setup` in scope) needs
  them carried onto `PaperTrade` (a cross-aggregate enrichment so the Performance/Alerting consumers can segment by
  setup/killzone). **(2e/host)** guard that the management candle's timeframe == the trade's `TriggerTimeframe` (so
  the §2.5.1-step-9 time-exit window can't drift); serialize the single demo-account write path (`xmin` concurrency);
  add **symbol-scoped** repo queries (`GetActive`/`GetOpen` currently load all symbols + filter in memory).
  **(minor)** publish-after-commit is at-most-once (not a transactional outbox); a same-bar open-then-close DTO reads
  `Status=Closed` on both events (consumers key on event TYPE); `PaperTradePartialClosed`/`StopMoved` have no contract
  (`PaperTradeFilled` unused). csproj: Application += `Microsoft.Extensions.Options.ConfigurationExtensions` + DI.Abstractions
  10.0.9; Infrastructure bumped Configuration/.Json 10.0.0→10.0.9 (cleared the pre-existing stale pins).
- **NEXT — 2e (the runnable Host):** DI the `PaperTradingDbContext` (+ connection string), `AddPaperTradingPersistence`,
  `AddScanningModule`/`AddPaperTradingModule`, and `AddMessaging` over the module Application assemblies; a Replay-feed
  `ScannerHostedService` driving `MarketDataIngestor` → `CandleIngested`; SignalR push + bus-backed REST. Then the app
  RUNS end-to-end.

**🏁 WP7 slice 2e — the runnable Host (issue #90, branch `feature/#90-runnable-host`, PR #91) — DONE. THE BACKEND
RUNS.** The composition root now assembles the entire ICT-gated loop, proven by an integration test that publishes
`SetupConfirmed` into the REAL booted Host and reads the persisted trade back from REAL Postgres. Built by a
general-purpose agent; gated **pr-reviewer APPROVE** (2 Should-fixes applied) + **guardrail 7/7**. 593 unit, 23 arch,
**17 integration** (2 new `HostScanLoopTests` on Testcontainers Postgres), 0 warnings, format clean:

- **`Program.cs`** — `AddMessaging(Scanning + PaperTrading Application assemblies)` scans the two `CandleIngested`
  subscribers (Scanning's scan-advance + PaperTrading's trade-advance) + the `SetupConfirmed` subscriber; **Scanning
  passed FIRST** → deterministic fan-out (detect/confirm before manage). `AddScanLoop(config)` composes the rest.
- **`ScanLoopRegistration.AddScanLoop`** — `AddDbContext<PaperTradingDbContext>` on Npgsql (lazy connection — a bare
  Host boots even before a DB is provisioned; migrations-assembly mirrors the design-time factory), the persistence,
  the Scanning + PaperTrading modules, the validated `ReplayFeedOptions`, and the hosted service.
- **`ReplayScannerHostedService`** (`BackgroundService`) — idle when replay is **disabled (default)**; else loads the
  CSV fixture + ingests **inside a try/catch** (the S1 fix — the load is here, NOT an eager DI factory, so a bad
  fixture path is logged, never a startup crash) → `CandleIngested` → the full chain. **`ReplayFeedOptions`**
  (`Ict:MarketData:Replay`: `Enabled`/`FixturePath`) is `ValidateOnStart`-gated (the S2 fix — `Enabled` with a blank
  path fails fast). Host csproj += EF Core 10.0.4 + Npgsql 10.0.2; `appsettings` += the Replay section + the empty
  `ConnectionStrings:PaperTrading`.
- **Integration tests (Testcontainers):** **A** boots the real Host (`WebApplicationFactory<Program>` over the
  container) — the whole scan-loop DI graph resolves + `LiveTradingEnabled=false`; **B** publishes `SetupConfirmed`
  onto the booted Host's bus → the real `SetupConfirmedHandler` opens + sizes + commits a trade to real Postgres →
  asserted via `IPaperTradeRepository.GetOpenAsync()`. Proves the composition + persistence end-to-end in the real Host.

**🎯 THE RUNNABLE BACKEND IS COMPLETE.** `dotnet run --project src/IctTrader.Host` composes + boots the full ICT-gated
modular monolith; enabling `Ict:MarketData:Replay` + a CSV fixture + a Postgres (`docker compose up postgres`, apply
migrations) drives it end-to-end: **feed → `CandleIngested` → Scanning detectors+FSM → `SetupConfirmed` → rehydrate →
paper trade opens/arms → candles advance → settles**, all on the in-memory bus, every decision in the pure §2.5 domain.

**Overnight batch — runnable backend PROVEN on real OANDA data + live dashboard (PRs #93–#113, merged). 🏁** The
runnable backend went from "composes + boots" to **producing real graded setups, paper trades, and performance on 2.7
years of real EUR/USD M5**, surfaced on a live React dashboard. Shipped (each `pr-reviewer` + guardrail gated, several
merged directly green per the operator's "don't wait for CodeRabbit, just merge and continue"):

- **docker-compose + verified-running (#93).** `postgres:17-alpine`, container `icttrader-postgres`, **host port 55432**
  (avoids local clashes), healthcheck. Apply migrations: `PAPERTRADING_CONNECTION_STRING=... dotnet ef database update
  --project src/Modules/PaperTrading/Infrastructure --startup-project src/IctTrader.Host`.
- **OANDA-practice read-only feed (#95).** `OandaMarketDataFeed : IMarketDataFeed` (`MarketData/Infrastructure/Feeds/`)
  — read-only by SHAPE (only `/v3/instruments/{i}/candles` GETs, no order path), typed `HttpClient`, backfill +
  optional resilient live poll (`from=<watermark>` gap-free), `OandaCandleParser` (RFC3339-ns→UTC, complete candles
  only). `OandaFeedOptions` (`Ict:MarketData:Oanda`, fxPractice host default, token via env). **Provider selector**
  `Ict:MarketData:Provider` = `Replay | Oanda`; `MarketDataIngestionHostedService` drives EITHER (renamed from
  `ReplayScannerHostedService`). **The .NET process's outbound HTTPS is BLOCKED in the default Bash sandbox** — run
  feed/DB commands with `dangerouslyDisableSandbox: true` (curl works; the .NET socket doesn't).
- **bus-backed REST + Performance (WP6) + Alerting (#97/#99/#107/#109).** `/api/trades/active` (`GetActiveTradesQuery`),
  `/api/performance` + `/api/equity` (pure `PerformanceCalculator` §5.3 R-based, `PerformanceState` singleton fed by
  `PaperTradeClosed`), `/api/alerts` (**Alerting module** — bounded `AlertLog` fed by `SetupConfirmed`/`PaperTradeOpened`/
  `PaperTradeClosed`, serving the §2.5 reason verbatim), `/api/chart/{symbol}` (**ChartCandleStore** per-(symbol,tf)
  ring buffer fed by `CandleIngested` + **RecentSetupStore** fed by `SetupConfirmed` → candles + setup overlays). All
  read-only projections; guardrail 7/7 each.
- **SignalR live push (#111).** Six Host-resident `IEventHandler<T>` broadcasters bridge the bus → push-only
  `TradingHub` (`CandleAppended`/`SetupDetected`/`TradeUpdated`/`PerformanceUpdated`), each log-and-swallow guarded.
- **OANDA history fetcher (#100/#101) + one-shot fetch mode (#104/#106).** `Ict:MarketData:Oanda:FetchHistory=true`
  runs the Host as a STANDALONE backward-paginating CSV exporter (`OandaHistoryFetcher` + `CandleCsvWriter` +
  `HistoryFetchHostedService`) then stops. **Fetched 200k EURUSD-M5 + 200k GBPUSD-M5 candles (Oct 2023 → Jun 2026) to
  `data/*.csv`** (gitignored). Two fetch-mode DI/bind fixes: branch `Program.cs` on `fetchHistoryMode` (#104), register
  the bus singleton in fetch-mode so the bus-typed GET endpoints bind (#106).

**🐛 CRITICAL scanner bug — config binder duplicated `ActiveStyles` (#113) — FIXED.** The .NET configuration binder
**appends** bound array items onto a pre-populated collection initializer instead of replacing it, so
`Ict:Scanning:ActiveStyles=["Intraday"]` bound onto the `[Intraday]` default became **`[Intraday, Intraday]`** — the
candle handler fed every candle to the same per-(symbol,style) singleton scanner **twice**, corrupting its window/swing/
FVG state so the FSM **never confirmed a setup in the running Host** (a 200k backtest produced 0; a direct `ScanSession`
over the same data confirms 39). Fix: default `ActiveStyles`/`ActiveKillzones` to **empty** so config replaces, and
apply the ICT default + de-dup via new **`ResolvedActiveStyles`/`ResolvedActiveKillzones`** accessors the scanner
consumes (`CandleIngestedHandler` uses `ResolvedActiveStyles`). Added an operator-visible "Setup confirmed" log + 3
binding-regression integration tests. **CONVENTION:** any operator-selected collection POCO bound from config must
default EMPTY + resolve its business default in code (a non-empty initializer is silently prepended by the binder).

**✅ REAL-DATA BACKTEST PROVEN.** `Ict:MarketData:Provider=Replay` + `Replay:Enabled=true` + `Replay:FixturePath=
data/EURUSD-M5.csv` + Postgres drives the full chain on real data: **scan → confirm (graded B) → alert → arm → open →
manage (T1 partial + stop-trail + time-exit) → close → settle → performance**. Mid-run sample: 75% win, +1.02R avg,
trades closing TargetHit +2.50R / TimeExit +0.95R / breakeven, alerts carrying full §2.5 reasoning (sweep→MSS→FVG→OTE→
draw, even an order-block confluence). **The React dashboard runs LIVE on this** (`web/ict-dashboard` with
`VITE_USE_MOCKS=false`, Vite proxy → Host on `:5080`): real candlesticks + Alerts feed + Performance + equity curve
(screenshot `ict-dashboard-live.png`, gitignored). To reproduce: `docker compose up -d postgres`; run the Host with the
Replay env on `--urls http://localhost:5080 --no-launch-profile`; `cd web/ict-dashboard && VITE_USE_MOCKS=false npm run dev`.

**Perf + chart notes (follow-ups):** the backtest is slow (~15-20 min for 200k) because the PaperTrading candle handler
reloads active aggregates from the DB **every candle** (DB-as-state) — fine for correctness, a future batching/in-memory
warm-cache would speed backtests. The chart shows only the **last ~1500 candles** (in-memory `ChartCandleStore`); candles
are **not persisted**, so the chart cannot show overlays for HISTORICAL setups (months back) — a "focus chart on alert"
needs **candle persistence (plan §7 `CandleEntity`) + a time-range `/api/chart?from=&to=`** to render old setups.

**WP9 E2E acceptance gate (issue #115, PR #116 → merged) — DONE. 🏁 The plan's mandatory gate is in place.** A
bus-driven `tests/IctTrader.E2E` (Reqnroll + Testcontainers Postgres) boots the REAL `Program` via
`CustomWebApplicationFactory<Program>` and drives the real in-memory bus + Scanning/PaperTrading/Performance/Alerting
handlers + EF persistence end-to-end: **PaperTradePipeline.feature** (a valid bullish London setup → paper trade →
**TargetHit** 100% win / setup+close alerts / advisory-only assertion; and a stop-out candle → **StopHit −1R**) +
**KillzoneClassification.feature** (10-example NY-boundary Scenario Outline via `KillzoneClock`, ICU `America/New_York`).
Bus-driven by design (the §2.5 model needs ~14k warmup candles, too slow to confirm per-test from raw candles): the
gate publishes a crafted Grade-B `SetupConfirmed` + the driving `CandleIngested` bars. **12 E2E green.** (Reqnroll note:
reference `Reqnroll.Tools.MsBuild.Generation` DIRECTLY or no `.feature.cs` is generated; the DI plugin is NOT used —
steps share state via the native `IObjectContainer`.)

**🏁 ALL WORK PACKAGES COMPLETE (WP0–WP9) + THREE adversarial audit-hardening rounds (CONVERGED) — full green suite:
build 0 warnings · 712 unit · 23 arch · 46 integration · 12 E2E (= 793 tests) · `dotnet format` clean.** The ICT 2022 Intraday FVG model is faithfully encoded end-to-end, the
runnable backend is proven on 2.7 years of real EUR/USD, the React dashboard runs live on it, and the mandatory E2E gate
guards the pipeline.

**Adversarial correctness-audit + hardening pass (workflow `wf_219f6533-2ba`, merging fix PRs #119/#123/#124/#125 for
issues #118/#120/#121/#122). 🔬** Under Ultracode I ran a 6-dimension adversarial audit of the COMPLETED system (config-
wiring, wire-contract, scan-trade-loop, guardrail/persistence, numeric/money, options-validation) with per-finding
skeptical verification: **28 findings examined, 23 confirmed real**, then fixed every High + Medium (+ most Low) via 4
parallel gated worktree agents. The big ones:

- **Same-bar look-ahead bias (HIGH, #118/#119):** a setup confirmed on candle N was opened AND advanced on candle N —
  its limit could fill / stop / runner could hit on the very bar that produced the signal (look-ahead distorting every
  paper-trade outcome + the Performance analytics). Fix: `PaperTradingCandleHandler` manages a position only from the
  bar STRICTLY AFTER its arm/open (`ArmedAtUtc/OpenedAtUtc < candle.OpenTimeUtc`); the legitimate within-`Advance`
  same-bar entry-then-exit re-feed is untouched. (Armed-trigger trades open at the fill bar's CLOSE, so they're first
  managed one bar later — conservative, never look-ahead.)
- **Config-binder duplication CLASS (HIGH, #120/#123):** the ActiveStyles append bug was systemic —
  `KillzoneEntryOptions.ActiveKillzones`, `RiskOptions.LossLadderPercents`, `OandaFeedOptions.Instruments`,
  `SdProjectionOptions.Multiples`/`NegativeFibOptions.Coefficients`, `SetupCandidateOptions.StandingConditions`,
  `ConfluenceOptions.RequiredConditions` ALL had non-empty defaults bound from settable sections. Swept them all to the
  empty-default + `Resolved*`/`Effective*` accessor pattern. **Also fixed the BROKEN selectable-killzone feature:** the
  operator's `Ict:Scanning:ActiveKillzones` never reached the detector (it read a different unbound section) — now
  `KillzoneEntryOptions` binds `Ict:Scanning` so the selection drives the `KillzoneEntryDetector` (dead
  `MarketContextOptions.ActiveKillzones` removed).
- **Dashboard R-unit rendering (HIGH, #121/#124):** the Performance panel rendered max-drawdown (R) as `%`, leaked the
  `999999` profit-factor sentinel, scaled the equity axis for dollars not cumulative R, and the mocks encoded a different
  unit model than the live backend (hiding it). Fixed + per-symbol price decimals (JPY 3 / metals 2 / FX 5).
- **Wire + persistence (MEDIUM, #122/#125):** `PaperTradeDto.Direction` now emits Long/Short (was Bullish/Bearish);
  the deterministic `SetupId` is threaded end-to-end so a redelivered/restart-streamed `SetupConfirmed` is a **no-op**
  (idempotent — was opening duplicate trades); the `TradePlan` JSONB now persists the full **N-tier** `TargetLadder`
  (SD tiers were dropped on round-trip).
- **Deferred (Low, non-blocking):** the dead `Ict:Killzones`/`Ict:Time` appsettings sections (SymbolScanner hardcodes
  `KillzoneSchedule.CreateDefault()` + NyClock uses the IANA id directly — the sections are unread; remove or wire
  later); symbol-scoped repo queries (`GetOpen`/`GetActive` load all symbols then filter — harmless under the
  single-symbol feed). The audit script is saved (resumable) for a future re-audit round.

**Round-2 audit + fixes (workflow `wf_eb4c7e75-736`; issues #127/#128/#129/#130 merged as PRs #131/#132/#133/#134).**
A second adversarial round (regression-of-the-fixes, concurrency, feed/security, resilience, domain-math, frontend depth,
test-coverage) found 22 confirmed (1 High, 9 Medium, 12 Low) — severity DOWN from round 1, converging. Fixed the High +
key Mediums:

- **Entry-leg spread never booked (HIGH, #127/#131):** the orchestrated lifecycle charged only the EXIT crossing — net
  P&L overstated by one entry-leg spread per trade (§5.4 violation). `ExitManager` now folds `ComputeEntryLeg` into the
  two whole-position terminal closes (the T1 scale leg stays exit-only) so the round trip == `ExecutionCostModel.Compute`.
  Also floors the T1 partial to the lot step. **(Supersedes the old "a clean open books no cost / entry spread rides the
  deferred exit-leg line" note — that was WRONG; the entry crossing is now booked at close.)**
- **Armed-trade off-by-one (MEDIUM, #128/#132):** a regression from the #118 look-ahead fix — a triggered armed trade
  (stamped at the trigger bar's CLOSE = next bar's open) was first managed on M+2 not M+1. Added `PaperTrade.ManagedFromUtc`
  (the trigger bar's OPEN — eligibility distinct from the fill-time `OpenedAtUtc`, +EF migration); the handler filters on
  it. Immediate stays N+1; neither looks ahead on its signal bar. Also: `MarketDataIngestor` isolates per-candle publish
  errors (one bad bar can't abort the stream); settlement commits BEFORE publishing its events.
- **OANDA live-poll resilience (MEDIUM, #129/#133):** the poll now survives transient timeouts/parse errors (still
  honoring cancellation; backfill stays fail-fast); `HistoryMaxCandles` is bounded.
- **Dashboard live mode (MEDIUM, #130/#134):** live build now renders the host's chart overlays (was zero — `useOverlays`
  hit an always-throwing stub), shows inline error states, **connects SignalR** (was poll-only dead code), and the candle
  merge is bounded/ordered/de-duped with incremental chart updates + seek-on-focus.
- **Deferred (Low/coverage, non-blocking):** the bus throw-propagation semantics (a core-handler throw still aborts the
  dispatch — by-design fail-fast, broadcasters already guarded); a raw-candle→confirmation test through the real 14-detector
  pipeline + a replay-feed E2E (both need the ~14k-candle warmup, so a fast deterministic fixture is hard — the bus-driven
  E2E + seeded ScanSessionTests cover the seam); JSONB back-compat for the pre-N-tier shape; DB-retry; unknown-symbol
  FX-coercion; the "focus chart on alert" cross-timeframe seek (needs `triggerTimeframe` on the alert/trade DTOs).

**Round-3 audit — CONVERGED (workflow `wf_1069cd6a-49e`; issue #136 merged as PR #137).** A focused third round (regression
of the round-2 fixes, deepest domain money/fidelity math, persistence/restart) found **4 confirmed, ALL Low (0 Critical/
High/Medium)** — the severity trajectory (round 1 Critical/High-heavy → round 2 declining → round 3 all-Low) confirms the
audit loop is **dry**. All 4 fixed anyway: a consecutive-failure circuit-breaker in `MarketDataIngestor` (a deterministic
handler bug now surfaces at the host's Error boundary after N=50 failures instead of masked Warning noise + a falsely
"successful" empty run); the mid-series live-candle render (`setData` fallback preserving pan/zoom — `series.update` only
on a true last-bar move); accurate gap-through fidelity comments (the §5.4 gap/slippage worsening is a DEFERRED follow-on,
not "applied downstream" — the fill is at the resting level); and `ArmedEntry`'s Setup JSONB now round-trips
`StackedFartherBound` (was dropped — matters under the non-default `StrictFirstFvg`). **The system is now COMPLETE +
audited-clean across 3 rounds; the remaining backlog is the documented OPTIONAL additive follow-ons only.**

**Post-audit render-verification fixes (issue #139 → PR #140).** Screenshotting the LIVE dashboard against the real
backend surfaced two render-only bugs the audits' static review missed (the new error-UI correctly surfaced the first):
the client `fetchEquityCurve` still threw the old "not available until WP7" stub even though `/api/equity` is wired (→
Performance panel showed an error), and the chart auto-scaled to a ~4-pip window (the incremental-render refs weren't
reset on series (re)create/StrictMode-remount, so the initial load was mis-detected as an incremental `update` instead
of `setData`+`fitContent`). Both fixed + verified: the live dashboard renders real EUR/USD candles fitted to view, the
setup's entry/stop/target/draw overlays, the §2.5 alerts feed, and the Performance + equity panels (screenshot
`ict-dashboard-final.png`). **Lesson for the next session: after a frontend change, RENDER it live (screenshot) — a
green typecheck/vitest does not catch a fit/scale or a stale-stub render bug.**

**Operator session — backtests + NASDAQ index + Grade-A emitters + entry markers (PRs #143/#145/#147 for issues
#142/#144/#146; suite now 712 unit + 23 arch + 46 integration + 12 E2E = 793 green).**

- **Real backtests on the fetched history (the deliverable the operator asked for).** Driving the full 200k-candle M5
  Replay feeds through the live loop: **EUR/USD (Oct 2023→Jun 2026): 39 setups → 20 trades, 55% win, +0.18R avg, profit
  factor 1.59, max DD 2.83R, net +$187.48** (after the now-correct round-trip spread+commission) — a modestly profitable
  positive-expectancy result. **NAS100 (index, same window): 30 setups → 8 trades, 12.5% win, −0.23R, PF 0.70** — the
  index path works end-to-end (point geometry, setups in the index AM killzone) but the FX-default model is NOT profitable
  on NAS100 over this sample (small N; the index wants its own tuning). **The backtest is slow (~15-20 min/200k)** — the
  DB-per-candle reload is the bottleneck (documented follow-up: a warm in-memory aggregate cache).
- **NASDAQ-100 index (#144/#145).** New pure-domain `InstrumentCatalog` (`IctTrader.Domain/Instruments/`) resolves every
  symbol → `{InstrumentClass, SymbolSpec, ContractSpec, per-class option overrides}`; FX majors keep `FxMajor` (byte-
  identical, `None` overrides), `NAS100USD` → the Index profile (`PipSize=1.0` point, `TickSize=0.1`, value `1.0/point`,
  `LotStep/MinLot=1`). This is the §2.5.7 instrument-class split: with the catalog, `NAS100USD` carries
  `InstrumentClass.Index`, so the ALREADY-CORRECT `KillzoneClock.ClassifyIndex` (AM 08:30–11:00, last-entry 10:40) finally
  activates, plus the 08:30 macro reference open (`UseMacroOpenReference` on for index) and point-based stops/costs
  (index `MinStopDistance≈10pts`, `StopBuffer≈2pts`, spread `≈1.0pt`, commission 0 — all INVENTED-flagged). `SymbolScanner`/
  `SetupConfirmedHandler`/`TradeOrchestratorFactory` resolve via the catalog (was hardcoded `FxMajor` — the flagged
  prerequisite). Dashboard selector gains NAS100. **To scan it live:** OANDA provider + `NAS100_USD` in
  `Ict:MarketData:Oanda:Instruments` (normalizes to `NAS100USD`).
- **Grade A now reachable — the 4 optional confluence emitters (#146/#147).** `OpenPriceReferenceDetector` (0.50, price
  vs the 08:30/midnight reference open agrees with bias), `MacroTimeDetector` (0.45, NY macro windows 08:30/09:30/13:30/
  15:00 ±10m INVENTED), `CleanPriceActionDetector` (0.40, displacement-leg Σ|body|/Σrange ≥ 0.60 INVENTED — the HRLR
  inverse), `CalendarDriverDetector` (0.35, a same-day driver event outside the blackout, distinct from `CalendarClear`
  per TGR-3). **Grading-safe by construction:** these weights were ALREADY in the constant Σ=9.75 universe, so the
  emitters only add to the numerator when matched — no existing grade drops; the bare-RequiredConditions setup still
  scores 63. Grade A proven (required 6.15 + OteZone 0.70 + OPR 0.50 + MacroTime 0.45 = 7.80/9.75 = 80). The §2.5.8
  long-tail (SMT/Breaker/Power-3/weekly/Sunday-gap) stays deferred — per the ICT spec they must be NON-scoring or reuse
  an existing weight, never a NEW `ConfluenceCondition` weight (that would change Σ and shift every grade).
- **Chart: entry is now a POINT (#142/#143).** An arrow marker at the setup's exact `detectedAtUtc`+entry price (up/down
  by direction), so the operator sees WHEN the trade enters, not just a horizontal level — alongside the stop/target lines.

**To see it (2 terminals):** (1) `docker compose up -d postgres`; apply migrations; run the Host
with the Replay env on `--urls http://localhost:5080 --no-launch-profile` pointed at a `data/*.csv`; (2)
`cd web/ict-dashboard && VITE_USE_MOCKS=false npm run dev` → `http://localhost:5173`. **NOTE: the .NET process's outbound
HTTPS + localhost-DB connections are BLOCKED in the default sandbox — run those with the sandbox disabled.**

**Still to come (OPTIONAL follow-ons, all additive — the plan is otherwise complete):** **candle
persistence + historical/time-range chart** (so the centerpiece chart shows any setup's FVG/OTE/entry-stop-target
overlays for setups older than the in-memory ~1500-candle window); the tracked enrichments (candle↔trade timeframe guard,
symbol-scoped repo queries, per-instrument `SymbolSpec` into `EntryFillEvaluator` for EG-3, the `Ict:Detection:Fvg`
binding into OTE/draw, `WindowCapacity ≥ DisplacementLegMaxBars` cross-check); the §2.5.8 long-tail (SMT/Breaker, macros,
weekly bias, HRLR, Power-3, Sunday-gap) + cost follow-ons (slippage, session-stepped spread, swap); **backtest-speed
batching** (the DB-per-candle reload makes a 200k backtest ~15-20 min — a warm in-memory aggregate cache would fix it).
**The fix to wiring the operator's killzone selection:
`KillzoneEntryDetector` reads `KillzoneEntryOptions.ActiveKillzones` (its own section), so `Ict:Scanning:ActiveKillzones`
does NOT currently change the detector's hunt-set — reconcile these two ActiveKillzones sources.**

**Process cadence (per the operator):** keep the ICT gate strict (`ict-domain-expert` + guardrail + `pr-reviewer`,
concurrent) but move faster — build directly from the locked design (skip the separate pre-spec when pinned), ship
bigger complete slices, and reserve the heavy ~600k-case adversarial driver for numeric/money-math slices (it fuzzes
numeric correctness, not ICT fidelity). Under Ultracode: settle subtle ICT calls with a single ict-domain-expert spec
(or a design judge-panel for wide design spaces), implement via `ict-detector-engineer`, then adversarially verify.

**The §2.5 fidelity backlog is COMPLETE (all 11 register slices merged) AND the runnable backend (WP7) is COMPLETE +
PROVEN on real data** (see the overnight-batch milestone above: feed/scan/trade/persist/alert/perf/SignalR + the live
dashboard). Optional domain follow-ons that remain: **TGR-1/2 Slice B** (SD-as-primary/fallback draw,
`AllowSdAsPrimaryDraw` — touches `DrawOnLiquidityDetector` + the RR gate, fires only when no untapped opposite pool
qualifies) and the §2.5.8 long-tail (SMT/Breaker, session macros, weekly bias, HRLR, Power-3, Sunday-gap) — all additive.

Remaining wiring/polish (all in the overnight-batch follow-ups above): the `WindowCapacity ≥ DisplacementLegMaxBars`
cross-check, the `Ict:Detection:Fvg` binding INTO the OTE/draw detectors, the per-instrument `SymbolSpec` into
`EntryFillEvaluator` (EG-3); **candle persistence + time-range chart** (plan §7) for historical setup overlays; the
**WP9 Gherkin E2E** gate; backtest-speed batching; the two-`ActiveKillzones`-source reconciliation; and the §2.5.8
long-tail + cost follow-ons (slippage / session-stepped spread / swap; lot-step flooring of the partial leg).
**WP8 frontend scaffold — MERGED (PR #34, issue #30); now runs LIVE on real backend data.**

**🏁 Trader-grade backtest lab + optimizer + multi-page UX + k-of-n relaxation (plan §15; PRs #150/#152/#153/#155/#157,
issues #149/#151/#154/#156 — all MERGED).** Turned the system from "runs + scans" into a usable paper-trading platform
the operator drives: define a period/style/portfolio/risk, backtest it, optimize across assets/timeframes/styles, and
inspect every trade + the live account. Suite **730 unit + 23 arch + ~54 integration + 12 E2E**, 0 warnings, format
clean; frontend typecheck/lint clean + 69 vitest. The OANDA fetch was extended to a full **M1→D1** ladder (D/W
granularity added, `OandaCandleParser` maps D→D1/W→W1) so all four styles backtest on native timeframes
(`data/{SYMBOL}-{TF}.csv`, gitignored).

- **(A, #150) Read-API for live visibility.** Extended `PaperTradeDto` (close reason, gross/net P&L, costs, NetR,
  lifecycle, current stop, exit price, risk budget, timeframe, managed-from, breakeven-armed); `GetClosedAsync`/
  `GetAllAsync` + `GetTradesQuery` → `GET /api/trades?status=&symbol=`; `AccountStatusDto` + `GET /api/account`;
  Host-owned `ConfigStatusDto` + `GET /api/config`. Read-only projections.
- **(B, #152) In-memory `BacktestEngine`** (`src/IctTrader.Host/Backtesting/`) REUSES the pure §2.5 domain
  (`SymbolScanner` + `TradeOrchestrator` + throwaway `PaperAccount`) synchronously over a CSV — no bus/DB, deterministic,
  seconds-fast, **no detection/fill/cost/sizing logic reimplemented** (no-look-ahead preserved). `POST /api/backtest` +
  `GET /api/backtest/datasets`; pure `CandleAggregator` + `TimeframeExtensions`; per-run risk override on
  `ITradeOrchestratorFactory.Create`; `PaperTradeDtoMapper` made public.
- **(C, #153) `BacktestOptimizer`** — sweeps symbols × styles × timeframes × risk (× k-of-n) → ranked leaderboard;
  datasets cached per (symbol,tf), bounded grid, concurrent. `POST /api/backtest/optimize`.
- **(D, #155) Trader dashboard** (`web/ict-dashboard`, react-router) — **Live · Trades · Backtest · Optimizer**: Live
  Account/Config panel; full sortable Trades history (status/close-reason pills/net-P&L/R/filters/totals); Backtest Lab
  (KPI tiles + Recharts equity curve + trades); Optimizer leaderboard (row → drills into the lab). Mocks cover every new
  endpoint. **Render-verified live (all 4 pages)** — caught + fixed a starting-balance number-input `step` that made the
  default 10,000 invalid; added a date range to the Optimizer. "Advisory · Paper only" everywhere; no execute control.
- **(F, #157) Configurable k-of-n required conditions — the operator's idea.** `ConfluenceOptions.MinRequiredConditions`:
  **null = strict canonical §2.5 (byte-identical)**; k<n = an EXPLICITLY non-canonical, experimental relaxation — a setup
  confirms with k of the n required conditions ONLY if its weighted §2.5.4 score still clears the alert floor (grading
  handed back to the score; strict-complete keeps TGR-4 ≥B, relaxed-partial graded by band). Threaded through
  `SetupScorer` + scanner factory + backtest (per-run) + optimizer (sweep dimension) + UI (backtest input, optimizer
  multi-select, leaderboard k/n column). **CONVENTION: any deliberate §2.5 deviation must be default-off + opt-in +
  flagged, so the canonical model stays default and tests stay byte-identical.**

**📊 BACKTEST + TUNING FINDINGS (full OANDA history via the optimizer):** **EUR/USD M15 Intraday strict = the best FX
combo (15 trades, 60% win, +0.28R, PF 1.97)**; EURUSD M5 PF 1.40; GBPUSD M5 PF 1.16 (marginal). **NAS100 needs its own
tuning: strict M5 PF 0.70 (losing) → k=6-of-8 PF 1.78, +0.36R, +6.3%** — proof the k-of-n relaxation pays off PER ASSET
(EURUSD prefers strict; NAS100/GBPUSD benefit from relaxation). Strict all-AND yields **0 Swing/Position trades** on the
fetched history — the optimizer/relaxation is how the defensive model is made productive on harder assets/styles. The
optimum is per (asset, timeframe, style, k); re-run a sweep to refresh.

**To run the trader UX (2 terminals):** `docker compose up -d postgres`; run the Host with
`ConnectionStrings__PaperTrading=Host=localhost;Port=55432;Database=icttrader;Username=icttrader;Password=icttrader_dev`,
`Ict__Backtest__DataDirectory=data`, Replay off, `--no-launch-profile -- --urls http://localhost:5080`;
`cd web/ict-dashboard && VITE_USE_MOCKS=false npm run dev`. Backtest/Optimizer need no DB (in-memory); Trades/Account
need Postgres. **The .NET socket is sandbox-blocked — run host/feed/test cmds with `dangerouslyDisableSandbox: true`.**

**Still-open follow-ons (additive):** bake the k-of-n + per-pair tuned settings as LIVE per-instrument defaults via a
config-augmentable `InstrumentCatalog` (`Ict:Instruments:*`); NAS100 index-specific detector/killzone tuning; speed up
the full-range optimizer sweep (bound the period or add a warm candle cache — M5 full = 200k × many combos); candle
persistence + time-range chart for historical overlays; the §2.5.8 long-tail. **Git cadence note:** after a merge+sync
the working branch is `main` — remember to `git switch -c` a fresh branch BEFORE editing the next slice (twice this
session edits landed on `main` and had to be moved to a branch; harmless but avoidable).

**🏁 Per-instrument tuned defaults + optimizer feature-subset search (PRs #160/#162, issues #159/#161 — MERGED).** Two
slices that answer "tune each pair, and use it": the optimizer now searches WHICH concepts to require (not just how
many), and a winning per-pair setting becomes the LIVE default. Suite **736 unit + 23 arch + 55 integration (1 skip) +
12 E2E**, 0 warnings, format clean; frontend typecheck/lint clean + 69 vitest.

- **(#160) Per-instrument config seam (`Ict:Instruments`).** `InstrumentOptionOverrides.MinRequiredConditions`
  (+ later `RequiredConditions`) + `OverlayWith`; `ConfluenceOptions.WithInstrumentOverrides` applies the symbol's baked
  gate with **explicit-per-run precedence** (the scanner applies it LAST); `ScannerOptions.WithInstrumentOverrides`
  threads it into the confluence FSM. A `ConfigurableInstrumentRegistry` overlays `Ict:Instruments:Overrides:<sym>` on
  the built-in catalog (config wins where set, built-in index geometry survives), registered in the Host BEFORE the
  modules so scanner/orchestrator/backtest all resolve it.
- **(#162) Optimizer feature-subset search.** `ConfluenceOptions.WithRequiredConditions` (per-run subset); the backtest
  takes `RequiredConditions`; the optimizer sweeps subsets — explicit `RequiredConditionSets` or auto `LeaveOutUpTo`
  (drop up to k of the **non-MSS** required to optional; `DisplacementMss` is NEVER dropped — the FSM needs it to lock
  direction). Each leaderboard row reports its required subset; the dashboard Optimizer gained a "drop up to" control +
  a Dropped column. The winning subset bakes per-instrument via `InstrumentOptionOverrides.RequiredConditions`.

**📊 KEY TUNING FINDING (the subset search beats the count):** **NAS100 trades best WITHOUT requiring an FVG** —
requiring `FvgPresent` filtered out good index trades, so dropping it to optional/scored gave **PF ~1.8 (16 trades) vs
the strict all-8 PF 0.7** over the full history (more explainable + better than the blind 6-of-8 count, PF 1.78).
**EURUSD stays strict** (M15 PF 1.97, best by ending balance). So `appsettings` bakes **`Ict:Instruments:Overrides:
NAS100USD:RequiredConditions` = the 7-concept set (FvgPresent optional)**; a DEFAULT NAS100 M5 backtest now runs that
subset (PF 1.8) with no per-run override, EURUSD M15 stays strict. **CONVENTION: a baked per-pair tuning result lives in
`Ict:Instruments` (operator-visible), the strict §2.5 model stays the global default, and an explicit per-run backtest/
optimizer value always wins over the baked one.** Re-run the optimizer `leaveOutUpTo` subset search to retune.

**Note (appsettings JSON comments):** `appsettings.json` uses `//` comments throughout — the .NET config provider
allows them (the Host boots fine), but the IDE's strict-JSON linter flags them as errors. They are false positives;
keep the established commented style (it documents every invented/derived number).

**🏁 Live settings (UI, no-restart) + economic-calendar feed (plan §15; PRs #165/#167/#169, issues #164/#166/#168 —
MERGED).** The operator can now SEE and TUNE the model from the dashboard, live, and the FOMC/NFP gate finally fires
from real data. Suite **751 unit + 23 arch + ~50 integration + 12 E2E**, 0 warnings, format clean; frontend
typecheck/lint clean + **73 vitest** + production build green. Each slice gated (guardrail 7/7; slice 3 also
ict-domain-expert CONFORMANT + pr-reviewer APPROVE) and **render-verified live** (Playwright against the real Host).

- **(slice 1, #165) Runtime-mutable settings store — live, no restart.** `IRuntimeSettings`/`RuntimeSettings`
  (`Domain/Configuration/`): a thread-safe per-instrument override holder with a **monotonic `Revision`**, seeded once
  from `Ict:Instruments`. `ConfigurableInstrumentRegistry` reads it LIVE on every `Resolve`; `SymbolScannerRegistry` +
  `TradeOrchestratorRegistry` **evict their per-(symbol,style) caches on a revision change** so the next candle/backtest
  rebuilds with the new options — no restart. `GET /api/settings` + `PUT /api/settings/instruments/{symbol}` (validated;
  null body clears). **Proven live:** PUT EURUSD k=6 → a default backtest jumped 1→11 setups; cleared → back to 1.
- **(slice 2, #167) Settings UI page** (`/settings`, react-router + nav). **Per-instrument overrides are editable +
  live** — pick OR type any symbol (datalist from `InstrumentCatalog.KnownSymbols`), set/clear its k-of-n, its
  required-condition subset (must include `DisplacementMss`), and its per-pair cost geometry; the keyed-uncontrolled
  form never clobbers an in-progress edit; the mutation invalidates the settings query so the table re-reads live. A
  **comprehensive read-only global concept view** (confluence required-set + k-of-n + per-condition weights + grade
  thresholds + alert floor; risk base/portfolio-cap/hard-max + loss ladder + win-cycle + dip-recovery; execution
  spread/commission; active killzones/styles), projected by the Host from the bound `Ict:*` options via the resolved/
  effective accessors. Also **restored `npm run build`** (`tsc -b`), red since the read-API slice left 3 stale
  `PaperTradeDto` test fixtures + a recharts `Formatter` type (the merge gates ran `tsc --noEmit`, which excludes tests).
- **(slice 3, #169) Economic-calendar feed → the FOMC/NFP gate FIRES.** The §2.5.2 gate was complete but never fed
  (`MarketContext.LoadCalendar` was test-only → fail-open). New `IEconomicCalendarStore`/`EconomicCalendarStore` (Domain,
  revision-stamped) + `IEconomicCalendarSource` port; the `SymbolScanner` loads the store into its `MarketContext` on a
  revision change (null store = byte-identical no-op → tests/backtests unaffected). Host sources behind
  `Ict:Calendar:Provider`: **`Config`** (operator-supplied dates — offline default) + **`Fmp`** (Financial Modeling Prep
  HTTP, read-only GET, key via env, parser unit-tested); a background loader (source resolved per-refresh from a DI
  scope so the FMP `HttpClient` stays factory-managed) keeps the store current over a NY-date window around today; `GET
  /api/calendar` + a Settings calendar panel show the events + §2.5.2 blackout days. **Gold-standard live proof:** a
  backtest that produced **1 setup on 2024-02-14** produced **0** once that day was configured as FOMC — the gate
  withheld `CalendarClear`. **CONVENTION:** a calendar source only WITHHOLDS trading (never causes an order) — read-only
  by shape, no order path (guardrail 7/7).

**New conventions this batch:** (1) any operator-tunable LIVE setting rides the revision-stamped store + cache-eviction
seam (slice 1) — add a field + bump the revision, consumers rebuild on change. (2) The economic-calendar **loader window
anchors to NY "today"**, so a HISTORICAL backtest needs a wider `Ict:Calendar:LookbackDays` to cover its period (a future
enhancement could load events for the backtest's own range). (3) Global concept knobs are currently **view-only in the
UI** (the per-instrument override is the live-editable surface, which already covers per-pair k-of-n/subset/costs);
making specific global knobs (risk %, active killzones/styles) live-editable would extend the store + the scanner/
orchestrator factory overlay — a clean follow-on. (4) `Ict:Calendar` is documented (commented, `Enabled:false`) in
`appsettings.json` with `Fmp.ApiKey` sourced from env, never committed. **To enable the gate live:** set
`Ict:Calendar:Enabled=true`, a provider, and (Config) the FOMC/NFP dates — or an FMP key.

**🏁 Optimized-state retune + live demo run (issue #171, PR #172 → merged).** Re-ran the optimizer/backtests over the
FULL history (data-driven, not stale notes) to settle the best per-pair config, baked the winners as the LIVE defaults
in `Ict:Instruments`, and proved the optimized model end-to-end on a clean run. **Confirmed per-pair optima
(full-history Intraday):**
- **EURUSD → strict §2.5 (all 8), best on M15** — 15 trades, 60% win, +0.28R, **PF 1.97**, maxDD 1.33R (the single
  strongest combo; no override — strict IS its optimum).
- **NAS100USD → drop FvgPresent (7-concept), best on M5** — 16 trades, +0.41R, **PF 1.80** (already baked; reconfirmed).
- **GBPUSD → drop {LiquiditySweep, FvgPresent} (6-concept), best on M15** — 13 trades, 69% win, +0.62R, **PF 4.44**,
  +2.9%. **NEWLY BAKED (#172)** as `Ict:Instruments:Overrides:GBPUSD`. ⚠ AGGRESSIVE/EXPERIMENTAL: GBPUSD *loses* strict
  (M15 PF 0.95 / M5 PF 1.06 net-flat), and this is the only net-profitable config found — but it drops TWO core ICT
  concepts (the Judas sweep AND the FVG) on a 13-trade sample. Opt-in per-pair; the global model stays strict; flagged
  in the appsettings comment; re-validate before relying on it.
- **Method note:** the optimizer's `ProfitFactor` objective surfaces tiny-sample flukes (e.g. a 3-trade M30 PF 3.98) —
  filter for a meaningful trade count (≥~10) before trusting a PF/expectancy winner. A full 464-combo sweep took ~7 min;
  prefer targeted single full-period backtests (~1s each) when confirming a handful of candidates.

**Optimized live run (the proof, on a CLEAN DB):** dropped+recreated the `icttrader` DB, applied migrations, and ran the
best combo (Replay = **EURUSD M15, 2024→2026**, a gitignored trimmed `data/EURUSD-M15-live.csv`, ~62k candles, ~5 min)
through the real feed→scan→trade→persist→perf chain: **17 setups → 5 paper trades, 80% win, +0.82R avg, PF 5.12, maxDD
1.00R, equity +3.4%** — incl. a **Grade-A** EURUSD setup (order block + 08:30-macro confluence). The dashboard rendered
it live (Alerts feed with full §2.5 reasoning, Trades, Performance, Settings showing the GBPUSD 6/8 + NAS100 7/8
overrides). The calendar gate was OFF for this run (alerts read "calendar clear — no data loaded").

**To reproduce the optimized run (2 terminals, sandbox-disabled for the .NET socket):** `docker compose up -d postgres`;
reset+migrate if you want a clean slate (`DROP DATABASE icttrader; CREATE DATABASE icttrader;` via
`docker exec icttrader-postgres psql -U icttrader -d postgres`, then
`ConnectionStrings__PaperTrading=Host=localhost;Port=55432;Database=icttrader;Username=icttrader;Password=icttrader_dev dotnet ef database update --project src/Modules/PaperTrading/Infrastructure --startup-project src/IctTrader.Host`);
run the Host with that connection string + `Ict__Backtest__DataDirectory=<repo>/data` + `Ict__MarketData__Provider=Replay`
+ `Ict__MarketData__Replay__Enabled=true` + `Ict__MarketData__Replay__FixturePath=<repo>/data/EURUSD-M15-live.csv` on
`--urls http://localhost:5080 --no-launch-profile`; then `cd web/ict-dashboard && VITE_USE_MOCKS=false npm run dev` →
`http://localhost:5173`. Backtest/Optimizer are in-memory (no DB needed); Trades/Live/Performance need Postgres.

**CONVENTION (dev hygiene):** a backgrounded `dotnet run` Host keeps `IctTrader.Host.exe` alive and LOCKS the output
DLLs — a later `dotnet build` then fails with MSB3026 "being used by another process" (looks like spurious errors).
Before rebuilding, kill ONLY the project's host (not other repos' dotnet): `Get-Process IctTrader.Host | Stop-Process
-Force` (plus the `dotnet run` wrapper via `Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | ? CommandLine
-match IctTrader | % { Stop-Process -Id $_.ProcessId -Force }`), or stop the background task first.

**🏁 Michael-Huddleston research + instrument expansion (ES/SPX500, USDJPY) + retune (issue #174, PR #175 → merged).**
A deep ICT/Huddleston study driving a full-history OANDA optimization pass. Suite **753 unit + 23 arch**, 0 warnings,
format clean; ict-domain-expert CONFORMANT (Ep5 transcript: ES≡NQ methodology), guardrail 7/7.

- **Research — [docs/ict-huddleston-research.md].** Transcript-mined (his own words, all 41 episodes) + a WebSearch-only
  fan-out (WebFetch was platform-rate-limited; **WebSearch snippets are the workaround when WebFetch 429s**). His primary
  vehicles are the **NASDAQ (NQ) + S&P (ES) index futures** (the 2022 Mentorship IS a NASDAQ e-mini mentorship), then the
  USD majors **EUR/USD (his cleanest/primary), GBP/USD, AUD/USD, USD/JPY**; gold secondary (event-driven); **exotics/crosses
  avoided.** Risk **1% max (prefer 0.5/0.25)**, RR **2:1→8:1**, stop after 2 losses, news (FOMC/NFP/CPI) + HRLR avoidance,
  HTF daily-bias alignment is the biggest win-rate filter, Silver Bullet 10–11 NY.
- **ES/SPX500 added.** Generalised `InstrumentCatalog` to an index SET `{NAS100USD, SPX500USD}`; new
  `SymbolSpec.Index`/`ContractSpec.Index` factories (`Nas100` now aliases them, byte-identical). Fetched the full OANDA
  **M5→D1** history for **SPX500_USD, USD_JPY, AUD_USD, XAU_USD** into `data/` (gitignored) — the universe is now **7
  instruments**. **NEW INVENTORY:** `data/{EURUSD,GBPUSD,NAS100USD,SPX500USD,USDJPY,AUDUSD,XAUUSD}-{M1?,M5,M15,M30,H1,H4,D1}.csv`.
- **Retune (full-history backtests, this session):** **BAKED `Ict:Instruments:Overrides:USDJPY` = drop-FvgPresent (7
  required)** — M5 PF **2.40**, +0.49R, +4.6%, the strongest new instrument. **REVERTED the GBPUSD override to strict** —
  its prior "PF 4.44" was an overfit 13-trade subperiod; full-history GBPUSD == strict == PF 1.18 net-loss (kept honest).
  EURUSD strict (M15 PF 1.97, best) + NAS100 drop-FVG (M5 PF 1.80) kept; AUDUSD (PF 0.60) / XAU / ES (sparse, <10 trades)
  left strict/unbaked. (USDJPY bake is a 10-trade sample — flagged provisional, re-validate on a wider window.)
- **Fetch-mode bug FIXED (regression from the slice-1/2/3 endpoints).** `Ict:MarketData:Oanda:FetchHistory=true` crashed at
  startup ("Body was inferred but the method does not allow inferred body parameters") because the new settings/calendar
  GET endpoints + the backtest endpoint depend on services not registered in fetch mode. **`Program.cs` now boots the
  one-shot fetcher and `app.Run(); return;` BEFORE mapping the REST/SignalR surface** (fetch mode needs no API).

**🔬 VALIDATION — does our engine catch Michael's 2022 live trades? NO (an honest, important finding).** Extracted his
documented 2022 trades from all 41 episodes (8 best-dated: Ep11 NQ short ~Feb22 +204h; Ep07 NQ long Feb8 +$2,345; Ep10 NQ
short Feb17; Ep06 NQ short Feb3; Ep18 EURUSD short Apr7; Ep40 USDCAD short Jun21). **Our engine does NOT replicate them:**
it is **high-precision / low-recall** — the full §2.5 AND-gate confirms ~**2 setups/YEAR on M15** (EURUSD 15 trades over
2018→2026; NAS100 ~2 setups over Sep21–Mar22), while Michael **scalps M1–M5 discretionarily** (multiple/week). Verified:
0 catches in his Jan–Feb 2022 NAS100 window (even relaxed to k=4), and on his one direct-match FX example (EURUSD short
Apr-7) our engine produced an **opposite-direction long on Apr-8**. Root causes: (a) timeframe — we only have M15 for 2022,
he trades M1–M5; (b) mechanical AND-gate vs human discretion. **This is by design (precision over recall), not a bug.**

**📊 SETUPS-PER-DAY / "find more setups to follow" (operator ask).** ICT norm ≈ **1 quality setup/day/instrument**
(Silver Bullet ~1/day; "quality over quantity" — not every killzone fires). Our engine is far below that on M15 strict.
Measured recall (EURUSD, ~125 trading days): **M15 strict 0.08/day · M5 k=5 0.25/day · M1 k=5 0.40/day** per instrument.
**To surface a followable stream (~2–3 setups/day aggregate): run M5 (or M1) + k≈5 relaxation + all 7 instruments + both
killzones** — a "discovery/signal mode" (opt-in; the strict §2.5 model stays the global default). The Settings page (live,
no restart) is where the operator dials per-instrument k-of-n / required-subset to trade recall for precision.

**CONVENTION (web research under rate-limit):** the `deep-research` workflow uses WebFetch, which the platform sometimes
429s ("Server is temporarily limiting requests"); when it returns 0 sources, fall back to a **WebSearch-only** research
workflow (snippets are detailed enough to cite) — and the repo's own transcripts are the PRIMARY ICT source regardless.

**🏁 Single-origin deploy + Live-page chart/market-status/responsive UX (issues #177/#179, PRs #178/#180 → merged).**
The system is deployable as ONE self-contained instance at a single URL, with the Live chart rendering + a market-session
widget. 753 unit + 23 arch, 0 warnings, format clean; FE typecheck/lint clean, 78 vitest, build green; render-verified at
1480px + 760px.
- **Single-origin deploy (#178):** `Program.cs` `UseDefaultFiles()/UseStaticFiles()` + `MapFallbackToFile("index.html")`
  (last; `/api/*`+`/hubs/*` win) so the Host serves the built SPA + API + SignalR on ONE port (no proxy/CORS). `wwwroot`
  is gitignored (build artifact). **DEPLOY:** `docker compose up -d postgres`; migrate; `cd web/ict-dashboard &&
  VITE_USE_MOCKS=false npm run build` then copy `dist/*`→`src/IctTrader.Host/wwwroot/`; run the Host (Release,`--no-build`)
  with `ConnectionStrings__PaperTrading` + `Ict__Backtest__DataDirectory=<repo>/data` + Replay on
  `data/EURUSD-M15-live.csv` on `--urls http://localhost:5080` → whole app at **http://localhost:5080**. (Local only; no
  cloud Dockerfile yet.) Also fixed: **fetch-history mode crashed** (slice-1/2/3 GET endpoints need services not in that
  mode) — it now boots the fetcher and `app.Run(); return;` BEFORE mapping the API.
- **Live chart renders for ANY selected asset/TF (#180):** `GET /api/chart/{symbol}` falls back to recorded CSV history
  (`ChartHistory`) when the live ring buffer has no candles for that (symbol,tf) — the feed only fills the scanned series,
  so the chart was blank otherwise. The chart panel now fills its row height (the `.layout` grid row stretches).
- **Market-status (#180):** `GET /api/market-status` (`MarketStatus.Compute`) — FX open/closed (Sun 17:00→Fri 17:00 ET,
  weekend-aware), current ICT killzone session, and the next ACTIVE killzone open **while the market is open** (name +
  minutes + NY start; closed weekend → next TRADEABLE session, e.g. "LondonOpen Mon 02:00"), DST-aware via `NyClock`. A
  Live-page widget shows OPEN/CLOSED + session + a live next-open countdown.
- **Responsive UX (#180):** Live grid reflows 3→2→1 col (`<1200`/`<860px`), chart fills, header wraps, nav/control polish.

**CONVENTION (deploy / dev-server lifecycle):** launch the Host as a run_in_background task's MAIN command (not `dotnet
run &` inside a script — that orphans it). Performance/alerts/chart read-models are IN-MEMORY (reset on Host restart; the
account + trades persist in Postgres), so after a redeploy they repopulate as the replay re-runs.

**🏁 Senior-trader hardening — Daily Risk Guard + HTF daily-bias gate + ICT 2022 web research (this session, branch
`claude/ict-mentorship-app-test-6y5mj7`, PUSHED — see the SESSION WRAP at the end for the final pushed state).** Driven by
the operator's "make it perfect as a senior forex trader" + a fresh external-web deep-research pass. Suite **776 unit + 23 arch**, 0 warnings,
`dotnet format` clean; guardrail **7/7 PASS**; both features spec'd by `ict-domain-expert` (transcript-cited) before build.

- **Daily Risk Guard (§2.4/§2.5.5 circuit-breaker)** — the missing enforcement half of the loss model: the
  `RiskManager` ladder sized DOWN but never HALTED. New pure `DailyRiskGuard`/`IDailyRiskGuard` (`Domain/Trading/`) stops
  NEW entries for the rest of the NY day after N consecutive losses OR a realized daily-loss cap (Ep41 revenge/loser's-
  cycle, Ep37 "stop pushing buttons", Ep18 "walk away"). NON-scoring (Σ=9.75 untouched), consulted by
  `TradeOrchestrator.OnSetupConfirmed` → a halted day returns the new `ManagedPosition.None` (nothing armed/opened, no
  risk reserved); it NEVER touches already-open/armed trades and adds no order path (guardrail-clean — only WITHHOLDS).
  Loss = GROSS structural outcome (a cost-only scratch is a breakeven, mirroring `PaperAccount`); the cap is on NET
  realized P&L; resets at 00:00 NY. The caller owns the per-day tally — wired into the live `SetupConfirmedHandler`
  (repo `GetClosedAsync` filtered by NY date) AND the `BacktestEngine` (in-memory `closed` list). `DailyRiskGuardOptions`
  (`Ict:Risk:DailyGuard`): **Enabled=false (config default → existing tests byte-identical), recommended-ON for
  live/optimized**; thresholds are provenance-flagged community canon (N=3 = the 1%→0.5%→0.25% ladder exhausted; 2.0%
  daily cap), not Mentorship-verbatim. The orchestrator's guard params are OPTIONAL (null tally → unguarded path
  byte-identical), so all existing call sites compiled unchanged.

- **HTF daily-bias gate (`DailyBiasOptions.RequireReferenceOpenAgreement`, default OFF)** — the web/community #1
  win-rate filter ("a 5m entry must never contradict the daily bias"). The midnight-open agreement ALREADY existed as
  the scored `OpenPriceReferenceDetector` (0.50); the spec's key insight = PROMOTE it into a gate, not add a new feed.
  When ON, `DailyBiasDetector` ANDs `ReferenceOpen(premium)` agreement into the existing **`BiasAligned` (0.85)** match
  (bearish wants price ABOVE the 00:00-NY/08:30 open, bullish BELOW) — reusing the SAME DST-correct state the scorer
  reads, so the two can never contradict. STRENGTHENS the existing dealing-range bias (does NOT replace/duplicate);
  Σ=9.75 intact (only WITHHOLDS the match); `ctx.Bias` is STILL set so the MSS lock / PD veto / Judas read are
  unaffected; fail-CLOSED when no open is captured. Single-timeframe-safe (NO Daily feed). Settable globally via
  `Ict:Detection:Bias`; **per-instrument exposure via `InstrumentOptionOverrides` + the live `RuntimeSettings` seam is
  the documented follow-on.** Decisive recommendation before relying on it: measure (in a backtest) how often the
  already-present `OpenPriceReference` confluence DIVERGES from the required gates — if ~0 it's redundant, if material it
  genuinely tightens precision.

- **External web research — `docs/ict-web-research-2026.md`** (NEW): a cited 5-angle WebSearch fan-out (≈55 queries,
  WebFetch was egress-429'd so snippets sourced) on the ICT 2022 model, instruments/sessions/timeframes, risk +
  management, A+ confluences, and a **balanced critique strand** (no independently verified ICT track record; edge comes
  from selectivity + risk discipline, not the entry pattern; most public backtests are underpowered/regime-sensitive).
  It confirms the engine's design intent (high-precision/low-recall ≈ ICT's "1–2 quality setups/day" norm) and ranks the
  top quality levers: HTF daily-bias (built), risk discipline (built), LRLR-over-HRLR draws + SMT (follow-ons).

**⚠️ ENVIRONMENT CONSTRAINTS this session (Claude Code on the web, read-only-GitHub sandbox):** (1) **OANDA + all free
intraday data hosts (Yahoo, Stooq) are egress-policy 403-BLOCKED** — no fresh market data is reachable here, so NO new
backtests were run this session; the per-asset findings below are from the PRIOR documented OANDA runs. (2) **The .NET
SDK download hosts are 403** — the build/test loop runs inside the `mcr.microsoft.com/dotnet/sdk:10.0` **Docker** image
(`--network host`, proxy + CA passed) since `mcr` + `nuget` ARE reachable; `dockerd` must be started manually with the
proxy env. (3) **GitHub is READ-ONLY** — both `git push` (proxy 403) and the GitHub MCP `create_branch`/`push_files`
(403 "Resource not accessible by integration") are denied, so the 2 commits are **LOCAL ONLY** (a patch is produced for
the operator to apply + push). To finish: apply the patch on a machine with OANDA access + push perms, then run the
optimizer to validate the HTF-bias gate + Daily Risk Guard on real data.

**Per-asset best setups (from PRIOR documented OANDA full-history runs — NOT re-run this session):** EUR/USD → **M15
Intraday, strict §2.5** (PF 1.97, best FX); USD/JPY → **M5, drop-FvgPresent 7-of-8** (PF 2.40, baked); NAS100 → **M5,
drop-FvgPresent 7-of-8** (PF 1.80, baked, index AM killzone); GBP/USD → M15 strict (PF ~1.18, marginal); AUD/USD strict
(PF 0.60, weak); SPX500/ES + XAU/USD sparse/secondary. All optima are **Intraday**; Swing/Position strict yield 0 trades.
Recommended next: enable `Ict:Risk:DailyGuard` + (per-pair) `RequireReferenceOpenAgreement`, then re-run the optimizer.

**🏁 Silver Bullet macro overlay + per-instrument HTF-bias-gate exposure (same session, same branch, all gates green).**
Completing the operator's 4-item "make it perfect" set. Suite **784 unit + 23 arch**, 0 warnings, `dotnet format` clean;
frontend typecheck + lint + **78 vitest** + production build green. Both ict-domain-expert spec'd; ict-conformance PASS;
guardrail + pr-reviewer run.

- **Silver Bullet macro overlay (`Ict:Scanning:SilverBullet`, default OFF)** — an OPT-IN, NON-classifying time-of-day
  narrowing of the §2.5.2 `KillzoneEntry` RequiredCondition (NOT a new killzone — the frozen `Killzone` enum +
  single-classification + `LondonClose`-already-10–11 made a `SilverBullet` member collide, so the spec chose an overlay).
  New `SilverBulletOptions` (`ResolvedMacroWindows` default = the canonical 10:00–11:00 NY AM macro; 03–04 / 14–15 are
  opt-in); `KillzoneEntryDetector` takes an optional 2nd ctor arg and, when Enabled, AND-requires
  `context.NewYorkTimeOfDay(candle)` ∈ an enabled macro window (an INTERSECTION — never opens a disabled killzone), else
  NoMatch + an `EvidenceKeys.SilverBulletMacro` evidence tag. **NO new `ConfluenceCondition` → Σ=9.75 untouched.** New
  `MarketContext.NewYorkTimeOfDay` passthrough (the one DST-aware `NyClock` path). **PROVENANCE: the named "Silver Bullet"
  is NOT in the 2022 Mentorship (only the idiom — Ep10/Ep19); Ep17 actually stops FX entries at 10:00 and treats 10–11 as
  LondonClose (FX) / the tail of IndexAm 08:30–11:00 (index) — every SB window is flagged Primer/community.** For an INDEX
  the overlay NARROWS IndexAm to the macro (10:00–10:40 after the existing 10:40 cutoff); for FX the 10–11 macro needs
  `LondonClose` in the active hunt-set. Wired through `ScannerOptions` (new required field) / `SymbolScanner` /
  `SymbolScannerFactory` / `IctOptionsRegistration` / `appsettings`. Default-off → byte-identical (the existing
  single-arg `KillzoneEntryDetector` ctor is unchanged; existing tests untouched).

- **Per-instrument HTF-bias-gate override** — `InstrumentOptionOverrides.RequireReferenceOpenAgreement` (bool?) +
  `DailyBiasOptions.WithInstrumentOverrides` (mirrors `RiskOptions`), applied in `ScannerOptions.WithInstrumentOverrides`
  (the seam already resolves MarketContext/Liquidity/Fvg/Draw/Confluence per-instrument — DailyBias now joins). So an
  operator can require the HTF daily-bias agreement on the pairs where a backtest shows the `OpenPriceReference`
  confluence diverges from the gates, while keeping it OFF globally (strict §2.5 default). Exposed end-to-end on the LIVE
  surface: `Host/SettingsDto.cs` (`InstrumentSettingsDto.RequireReferenceOpenAgreement` + From/To mapping) → `GET/PUT
  /api/settings/instruments/{symbol}` → the React Settings page (a "Require HTF daily-bias agreement" checkbox;
  `types/api.ts` + `SettingsPage.tsx`). It rides the existing revision-stamped `RuntimeSettings` + cache-eviction seam, so
  it's live (no restart). Bindable from `Ict:Instruments:Overrides:<sym>:RequireReferenceOpenAgreement` too.

- **Per-asset tuned defaults (the 4th item) — VERIFIED, not re-baked.** The documented optima are already the live
  defaults (`Ict:Instruments`: NAS100USD + USDJPY = drop-FvgPresent 7-of-8; EURUSD/GBPUSD = strict). No fresh OANDA data
  was reachable this session to RE-tune, so no numbers were fabricated; the new per-instrument bias-gate knob is the
  MECHANISM to bake a per-pair HTF-bias result once the operator re-runs the optimizer on real data.

**Env note (unchanged): build/test ran in the `mcr.microsoft.com/dotnet/sdk:10.0` Docker image (.NET SDK hosts 403);
GitHub push works only via a user-supplied write token (the read-only integration 403s on push + MCP create_branch).**

**📊 REAL OANDA BACKTEST (fetched THIS session — egress policy changed mid-session, OANDA went 403→reachable).** Fetched 2
years (2024-06→2026-06) of OANDA-practice mid candles for 8 datasets via a paginating fetcher → `data/*.csv`, then drove
the in-memory `BacktestEngine` (Host in Docker, `/api/backtest`, no DB) at 1% risk, Intraday. **Baseline (live default
config, all new features OFF):**

| Asset | TF | setups | trades | win% | avgR | PF | maxDD | Notes |
|---|---|---|---|---|---|---|---|---|
| **NAS100USD** | M5 | 41 | 13 | 38% | **+0.46** | **1.83** | 3.0R | ⭐ best (baked drop-FVG 7-of-8) |
| EURUSD | M15 | 12 | 4 | 50% | +0.13 | 1.31 | 1.0R | best FX (strict) |
| GBPUSD | M15 | 12 | 2 | 50% | +0.01 | 1.07 | 0.4R | marginal |
| EURUSD | M5 | 34 | 18 | 44% | −0.07 | 0.81 | 3.4R | losing |
| USDJPY | M5 | 75 | 4 | 25% | −0.50 | 0.34 | 3.0R | losing (baseline) |
| AUDUSD/SPX500/XAUUSD | — | — | 0–3 | — | — | ≤1.25 or fluke | too few trades |

**Features-ON run (DailyGuard + HTF-bias gate enabled) — the key validation:** **USDJPY M5 FLIPPED loser→winner: PF
0.34→1.94, +0.40R, 29% win, +$341** (the bias gate removed the bad trades); **NAS100 held PF 1.83 with lower drawdown
(3.0R→2.0R)**; **EURUSD was OVER-filtered (12→5 setups, survivors were losers) → keep it strict.** This is the exact
"enable the HTF-bias gate PER-PAIR where it diverges meaningfully" thesis — and the per-instrument override built this
session is the mechanism (turn it ON for USDJPY, OFF for EURUSD). **The "few trades" finding is emphatically confirmed:
0–18 trades/asset over 2 years (high-precision/low-recall by design).** Small samples are noisy (AUDUSD PF 5–7 on 2–3
trades = fluke; trust only ≥~10-trade combos: NAS100, EURUSD-M5). **Best setup per asset (real data): NAS100USD M5 7-of-8
(PF 1.83) · USDJPY M5 7-of-8 + HTF-bias ON (PF 1.94) · EURUSD M15 strict (PF 1.31, best FX).** Re-run the optimizer over
the wider 2018→2026 history for the canonical bake (this window is only 2yr); `data/` stays gitignored.

**📊📊 FULL-HISTORY k-of-n VALIDATION (OANDA 2018→2026, ~210k M15 / ~600k M5 candles — the robust retune) + NAS100 bake
updated. THE 2-YEAR FINDINGS WERE LARGELY FLUKES.** Fetched the full history for the 5 candidates and swept
MinRequiredConditions (k of 8). With 16–166-trade samples the picture INVERTS the 2-yr window on every pair except NAS100:
- **EURUSD M15 → STRICT 8-of-8 wins: PF 2.20, +0.32R, 62% win, 16 trades** (relaxed k=7 → PF 1.42, k=6 → 1.30 — the 2-yr
  "k=6 better" was small-sample noise). **Keep strict** (the live default).
- **NAS100USD M5 → 6-of-8 wins decisively: PF 1.87, +0.40R, 37% win, 46 trades, ~1.9 setups/wk, +$617** — beats BOTH
  strict-8 AND the prior drop-FvgPresent 7-subset (both PF 1.62). **BAKED: `Ict:Instruments:Overrides:NAS100USD` changed
  from the drop-FVG `RequiredConditions` list to `MinRequiredConditions: 6`** (validated live: a default NAS100 backtest
  now runs k=6). The one robust relaxation win — more trades AND better quality (the k-saturation floor at 6 keeps it from
  garbage; DisplacementMss always gates).
- **USDJPY M5 → keeps the drop-FVG 7-subset: PF 1.48, +0.24R, 54 trades** (= strict-8 on full history; relaxing to k=6
  HURTS → PF 1.14). The 2-yr "needs the HTF-bias gate / losing PF 0.34" was the small window — full history strict is
  already positive. Comment updated to the honest PF 1.48 (the prior "PF 2.40" was a 3-yr subwindow).
- **GBPUSD M15 → weak everywhere (PF 0.95 strict / 0.99 k=7 / 1.06 k=6), EURUSD M5 ~breakeven (0.99/1.09).** No bake.

**LESSON (now a hard convention): a 2-year backtest is NOT enough to bake a per-pair tuning — validate on the FULL
2018→2026 history (100+ or at least ~15+ trades) first; small windows produce PF flukes (the EURUSD/GBPUSD/USDJPY 2-yr
"wins" all evaporated).** Strict §2.5 holds up well on the robust sample (EURUSD M15 PF 2.20); broad relaxation mostly
trades quality for frequency. Re-run `scratchpad/sweep_full.py` (or the optimizer) on refreshed data to retune.

**On "few trades" (operator's Q):** the strict all-8 AND-gate IS the cause; relaxing k DOES raise frequency (EURUSD M15
0.15→0.59 setups/wk at k=6; NAS100 0.31→1.88/wk) but usually LOWERS PF on FX — only NAS100 gets more-trades-AND-better.
Research (docs note): ICT's own cadence is ~1–2 A+ setups/day, "no trade is a good trade", quality-over-quantity; the
canonical lever for more statistical mass is MORE INSTRUMENTS + LOWER TIMEFRAMES (M1–M3 scalps run 10–15/wk), NOT
loosening confluence — though the community does both. "1–2 quality trades/week" is the legitimate swing/A+ target.

**🌐 FULL-UNIVERSE retune (10 instruments, OANDA 2018→2026 full history) — broadened toward 100+ trades + ALL validated
per-pair winners BAKED.** Fetched the rest of Michael's named set (AUD/USD, USD/CAD, NZD/USD M15; SPX500/US30 M5; XAU/USD
M15) and swept k-of-8 over the full history. **Aggregate 305 trades across the universe.** Picked by NET P&L (endingBalance after
costs), NOT gross PF — a gross PF > 1 can still be a NET loss. Per-instrument best (full history), now all in
`Ict:Instruments`:
| Instrument | TF | baked k | PF | trades | NET |
|---|---|---|---|---|---|
| **EUR/USD** | M15 | strict 8 (no override) | **2.20** | 16 | +$96 |
| **NZD/USD** | M15 | **7-of-8 (BAKED, new)** | **1.91** | 42 | +$86 |
| **NAS100** | M5 | 6-of-8 (baked) | **1.87** | 45 | +$619 |
| **USD/JPY** | M5 | drop-FVG (baked) | 1.48 | 54 | +$246 |
| **USD/CAD** | M15 | **7-of-8 (BAKED, new)** | 1.19 | 59 | +$141 |
| GBP/USD | M15 | strict (no override) | 0.95–1.06 | 21–64 | ≈breakeven (+$68 strict; relaxed net-loses) |
| AUD/USD | M15 | strict (no bake) | ≤0.79 | 15 | net-negative |
| SPX500 · US30 · XAU | M5/M15 | **0–7 trades** | — | — | — |

**NZD/USD M15 7-of-8 (PF 1.91, 62% win, +$86) is the strongest NEW instrument; USD/CAD M15 7-of-8 (PF 1.19, +$141) the
second** — both BAKED + verified live. **CRITICAL pick-metric lesson: USDCAD k=6 (gross PF 1.20) was a NET LOSS (−$44);
USDCAD k=7 (PF 1.19) is +$141 net — pick by NET P&L, not gross PF.** GBP/USD is ≈breakeven in every config (strict is its
net-best at +$68 — every relaxation net-loses), so it stays STRICT (no override). AUD/USD net-negative → strict. **XAU/USD (gold) + US30/Dow produced 0 trades
— they are NOT in the pure-domain `InstrumentCatalog`, so they fall back to FX-major money geometry and mis-size to zero
lots; adding gold + Dow `SymbolSpec`/`ContractSpec` profiles (like NAS100/SPX500) is a CODE follow-on before they can
trade/be tuned** (flagged in appsettings). **A profitable ~1–2-quality-setups/week followable stream is now the default
across {EURUSD, NZDUSD, NAS100, USDJPY, USDCAD} — all positive-expectancy on 16–62-trade full-history samples.** Re-run
`scratchpad/sweep_all.py` on refreshed data to retune.

**📌 SESSION WRAP — current operating state (branch `claude/ict-mentorship-app-test-6y5mj7`, all gates green, pushed).**
This session, on top of the completed §2.5 model + runnable backend, added (each ict-domain-expert spec'd → built →
ict-conformance PASS → guardrail 7/7 → pr-reviewer; **784 unit + 23 arch**, 0 warnings, `dotnet format` clean; FE
typecheck/lint/build + 78 vitest):
1. **Daily Risk Guard** (`Ict:Risk:DailyGuard`, default off) — stop-after-N-losses + daily-loss-cap halt.
2. **HTF daily-bias gate** (`Ict:Detection:Bias:RequireReferenceOpenAgreement`, default off) + **per-instrument override**
   (`InstrumentOptionOverrides` → live Settings page).
3. **Silver Bullet macro overlay** (`Ict:Scanning:SilverBullet`, default off) — narrows KillzoneEntry to the 10–11 NY macro.
4. **Two web-research docs** — `docs/ict-web-research-2026.md` (ICT 2022 model) + the trade-frequency findings in this file.
5. **Real OANDA backtests** (token now reachable; full 2018→2026 history fetched for 10 instruments) → **net-validated
   per-instrument bake** in `Ict:Instruments`: NAS100 6-of-8, USD/JPY drop-FVG, USD/CAD 7-of-8, NZD/USD 7-of-8 (EURUSD/
   GBPUSD/AUD strict). **Live default is a net-profitable ~1–2 setups/wk stream across 5 instruments.**

**CONVENTIONS added this session:** (a) validate a per-pair tuning on the FULL 2018→2026 history before baking (2-yr
windows produce flukes); (b) pick the per-pair config by **NET P&L after costs**, never gross PF (a gross PF > 1 can net-
lose). **Env:** build/test via the `mcr.microsoft.com/dotnet/sdk:10.0` Docker image (.NET SDK hosts 403, `mcr`+`nuget`
reachable; `dockerd` started manually with the proxy + restarted on idle); OANDA + GitHub push both work only via a
user-supplied token (the read-only integration 403s). `data/*.csv` (the fetched history) + `scratchpad/*` stay local.

**OPEN follow-on (CODE, not config):** add gold (XAU/USD) + Dow (US30) `SymbolSpec`/`ContractSpec` profiles to the
pure-domain `InstrumentCatalog` (mirror NAS100/SPX500) — without them both mis-size on FX geometry and produce 0 trades,
so they can't yet trade or be tuned. Then re-run `scratchpad/sweep_all.py` to bake their best config.

**🩺 Live-ops session — Postgres restart + OANDA live feed for ALL chart instruments + backfill resilience (branch
`fix/oanda-live-multi-instrument-resilience`, UNCOMMITTED).** Operator reported the running dashboard 500ing on
`/api/account` + `/api/trades/active` and a frozen "live" price. Diagnosed + fixed:
- **The 500s = the dev Postgres was down.** `icttrader-postgres` (host port 55432) had `Exited (255)`; every DB-backed
  query threw `Npgsql: connection refused` while the in-memory read-models (Performance/chart/market-status) stayed up
  (exactly which endpoints 500'd). Fix: `docker start icttrader-postgres` — the running Host's Npgsql pool reconnected
  on the next request (no Host restart needed). Account + trade history persisted intact.
- **No live price = no feed running.** appsettings ships `Provider=Replay` + `Replay.Enabled=false`, so nothing
  ingested and the chart served stale CSV-history fallback. Wired the **OANDA-practice live-poll** feed via env
  (`Ict__MarketData__Provider=Oanda`, `LiveStreaming=true`, `Granularity=M5`, `HistoryCount=500`, `PollSeconds=30`,
  `Instruments__0..4 = EUR_USD,GBP_USD,USD_JPY,XAU_USD,NAS100_USD` — the 5 chart-selectable symbols from
  `ChartPanel.tsx SYMBOLS`). Token validated against `/v3/accounts` (practice acct `101-004-39667063-001`) and
  persisted as a **User env var** `Ict__MarketData__Oanda__Token` (never committed). All 5 now stream today's M5 candles
  live; the chart updates as each candle closes. **Caveat:** the feed streams ONE granularity (M5 = the chart default);
  other chart TFs (M1/M15/H1) fall back to CSV history. Multi-TF-live would need a feed change.
- **CODE FIX — OANDA backfill resilience** (the reported fragility: a single transient refusal during startup backfill
  permanently killed ingestion; with 5 instruments over flaky egress it would fail almost every launch). Added to
  `OandaMarketDataFeed`: **bounded transient retry** on the backfill fetch (`OandaFeedOptions.BackfillMaxAttempts`=5 /
  `BackfillRetryDelaySeconds`=2, `Validate()`-gated) for connection-refused/408/429/5xx/timeout — **auth/4xx still fail
  fast** (a bad token never loops); plus **per-instrument skip when `LiveStreaming`** (one unreachable symbol is logged +
  skipped so the others keep streaming) while a **backtest stays fail-fast** (no silent data gaps). Pure classifier
  `IsTransientFetchFailure`; the live POLL path is untouched (it already self-heals). 3 new unit tests. Build **0
  warnings · 787 unit (+3) · `dotnet format` clean · defensive-guardrail-auditor 7/7 PASS** (still a read-only GET feed,
  practice host only, no order path). appsettings gained a commented OANDA example block + the two retry knobs.

**CONVENTIONS this session:** (a) **`dotnet test` (xUnit v2 / VSTest) must run under the DEFAULT Bash/PowerShell sandbox
— NOT `dangerouslyDisableSandbox:true`**: the disable-wrapper breaks the test-host↔runner loopback IPC ("Failed to
negotiate protocol" hang), but the default sandbox runs the suite fine (787 green in ~2s). The .NET Host's outbound
HTTPS (OANDA) and localhost calls (the running app on :5080, Postgres on :55432) still DO need
`dangerouslyDisableSandbox:true`. (b) To run the app live for all chart instruments: `docker start icttrader-postgres`;
launch the Release Host as a `run_in_background` task's MAIN command with the OANDA env above + the
`ConnectionStrings__PaperTrading` + `Ict__Backtest__DataDirectory` + `ASPNETCORE_URLS=http://localhost:5080`. (c) OANDA
resolves to two Cloudflare edge IPs and one intermittently **refuses** connections (~1 in 5) on this network — the new
backfill retry is what makes a multi-instrument live feed reliable through it. **TODO:** commit this branch (operator's
"commit only when asked" convention — left uncommitted pending the go-ahead); consider promoting the same bounded-retry
to the live-poll first tick and a multi-granularity live feed if multi-TF-live is wanted.

**🚧 PRODUCT-OVERHAUL PROGRAM (in progress this session — operator: "make it better; live discovery/signals, manual
take, live multi-TF feed, persist data, dismissible notifs, understandable settings, research more trades/week").**
Approved 10-slice plan at `C:\Users\Mostafa\.claude\plans\we-did-a-lot-abundant-quilt.md` (deep per-area designs in
sibling `…-agent-*.md` files). Built in sequenced vertical slices on branch `feature/slice1-instrument-profiles-silver-bullet`
(UNCOMMITTED — "commit only when asked"). Backend slices serialize (one `dotnet build` at a time — DLL-lock); frontend
runs in parallel (npm, disjoint `web/`). Each gated build 0-warnings + tests + format; guardrail/ict-conformance on the
slices that touch feeds/trading. **Research deliverable DONE:** `docs/ict-frequency-research-2026.md` (WebSearch-only —
WebFetch was 429'd; the key finding: a single instrument can't hit 5–15 setups/week — **breadth (basket) + M5 + the
HTF-bias gate + per-pair k-of-n relaxation only where full-history net-P&L proves it** is the faithful way; never gut the
sweep/MSS/displacement/killzone/bias core; faithful frequency adds we lack = Turtle Soup detector + the NY-PM Silver
Bullet window).

- **Slice 1 — DONE & VERIFIED (794 unit).** `InstrumentCatalog`: **US30USD** added as an index CFD (reuses Index
  geometry + AM killzone + macro ref); **XAUUSD moved OFF FX-major to a new METAL profile** (`SymbolSpec.Metal` pip 0.1 /
  tick 0.01 / 2-digit / InstrumentClass.Fx so it hunts FX London/NY; `ContractSpec.Metal` $0.10/oz per pip, 1-oz lot;
  `MetalOverrides` — gold no longer mis-sizes to 0 lots, proven by a PositionSizer test). All gold/Dow numbers
  CONVENTION/INVENTED-flagged. **NY-PM Silver Bullet is now a named first-class window** — `SilverBulletOptions.London/
  NewYorkAm/NewYorkPmMacro` + `AllMacroWindows` (default still the single 10–11; appsettings shows the 3-window opt-in;
  FX needs `Pm` in ActiveKillzones for 14–15).
- **Slice 2 — DONE & VERIFIED (805 unit + 23 arch, 0 warnings, format clean).** **Multi-granularity OANDA live feed:**
  `OandaFeedOptions.Granularities` (empty-default → REPLACES; resolves to `[Granularity]` when empty = byte-identical) +
  `MaxConcurrentFetchesPerPoll` (default 6, range [1,24]); `OandaMarketDataFeed` watermark is now a `(Instrument,
  Granularity)` composite and BOTH backfill + live-tail fan out over instruments×granularities bounded by a
  `SemaphoreSlim`, merged + chronologically re-sorted (downstream ingest stays sequential). Read-only-by-shape +
  resilience + the single-granularity `FetchHistory` path all preserved. **CI-safe: committed `Ict:MarketData:Provider`
  stays `Replay`** (a tokenless boot must not fail-fast / break Host-booting tests); appsettings carries a ready-to-
  uncomment OANDA live multi-granularity block (token only via `Ict__MarketData__Oanda__Token`). The operator runs live
  via those env vars (as before) — "wire live not CSV" = the capability + the documented env run, not a committed flip.
- **Slice 7 — DONE & VERIFIED (104 vitest, build+lint clean).** Dependency-free notification system under
  `web/ict-dashboard/src/notifications/` (module-level store via `useSyncExternalStore`; `ToastViewport` with a close
  button + pause-on-hover auto-dismiss; `NotificationCenter` bell + history; **errors STICKY** so a backend failure
  can't auto-vanish). `SystemHealthIndicator`/`useSystemHealth` is DECOUPLED from the toast store (dismissing a toast
  never hides a still-failing backend — tested). `AlertsFeed` rows are individually dismissible (✕, localStorage-backed
  `useDismissedAlerts`) + "Clear all"; error rows keep no dismiss. Live right-rail switched from cramped flex-wrap to
  `grid-template-columns: repeat(auto-fit, minmax(280px,1fr))`. Triggers: TradeUpdated open/close + sticky error toasts;
  `notifyNewOpportunity(...)` reserved for the future `SignalsUpdated` (slice 8).
- **Slice 9 — DONE & VERIFIED (121 vitest, build+lint clean).** `SettingsPage` rebuilt as a tab orchestrator (Quick
  setup · Instruments · Concept model · Discovery · Calendar) + a Simple⇄Advanced localStorage toggle. New: `PresetPicker`
  (Strict §2.5 / Balanced k=6 / Discovery k=4) that FILLS the per-instrument form with a vs-default diff; `InfoTip`
  accessible glossary popovers (`src/settings/glossary.ts`); `OverrideField` showing the inherited global default + a
  delta chip; extracted `InstrumentOverrideForm`/`GlobalConceptCard`/`EconomicCalendarCard`. **Wire contract UNCHANGED.**
  Discovery tab is a clearly-badged PREVIEW (scan matrix off `ConfigStatusDto` + an Auto/Manual concept) with a commented
  seam — the live scan-matrix + entry-mode editing wires when the backend discovery/entry-mode endpoints land (slice 6/8).
- **Slice 3 — DONE & VERIFIED (823 unit +18, 23 arch, 0 warnings, format clean).** Candle persistence + time-range
  chart. `ICandleRepository` (Domain, `GetRangeAsync`/`GetRecentAsync`/idempotent `AppendAsync`); a SEPARATE
  `MarketDataDbContext` (NOT extending PaperTradingDbContext — arch boundary; 23/23 green) + `CandleEntity` +
  `CandleConfiguration` (`candles` table, OHLCV `numeric(18,8)`, `timestamptz`, UNIQUE `(symbol,timeframe,open_time_utc)`,
  no xmin) + design-time factory + `CandleRepository` (UPSERT via raw `INSERT … ON CONFLICT DO NOTHING`) + the
  `AddCandles` migration. **Batched dual-write** (the bottleneck fix): `CandlePersistenceProjectionHandler` enqueues to a
  bounded Channel, `CandlePersistenceHostedService` drains in batches in its own scope (DB outage never fails scan
  dispatch); the in-memory `ChartCandleStore` stays synchronous. `GetChartRangeQuery` + `/api/chart/{symbol}?from=&to=`
  (DB range when present; else ring buffer→DB recent→CSV). `CandlePersistenceOptions` (`Ict:MarketData:Persistence`,
  default **Enabled=false**; `NoCandleRepository` no-op when off). 10 Testcontainers tests WRITTEN (Docker-gated — run
  `dotnet test tests/IctTrader.IntegrationTests` with Docker; apply the migration first via `dotnet ef database update
  --project src/Modules/MarketData/Infrastructure --startup-project src/IctTrader.Host`).
- **Slice 4 — DONE & VERIFIED (836 unit +13, 23 arch, 0 warnings, format clean).** Full-matrix
  (symbol,timeframe,style) scanning. New pure `IctTrader.Domain/Styles/StyleTimeframeMap.cs` —
  `StylesFor(tf, activeStyles)` returns active styles whose `TradeStyleClassifier.ResolvePolicy(style).EntryTimeframe == tf`
  (Scalp→M1, Intraday→M5, Swing→M15, Position→H4 from `Ict:TradeStyles`). `SymbolScannerRegistry` key →
  `(Symbol, Timeframe, Style)`; factory/scanner carry the TF (so `Setup.Timeframe`/`TriggerTimeframe`/the id hash are
  authoritative per cell); `CandleIngestedHandler` routes each candle by `StylesFor(candle.Timeframe, ResolvedActiveStyles)`.
  Single-TF M5 + `[Intraday]` is BYTE-IDENTICAL (proven by the unchanged determinism `ScanLoopTests`). The revision
  cache-eviction is unchanged. So scanning the "matrix" = enable more styles + more granularities (the feed) — each style
  on its canonical entry TF, ICT-faithful.
- **Slice 5 — DONE & VERIFIED (865 unit +29, 23 arch, 23 integration non-Docker, 0 warnings, format clean).** Signals
  ranking + feed. Pure `IctTrader.Domain/Confluence/SignalRanker.cs` (Grade→Score→RR→TF-priority→recency, weights from
  `SignalRankingOptions` `Ict:Signals`) + `SignalFeedStore`/`SignalRankingService` (Scanning.Application/Signals, bounded,
  id-keyed, recency-pruned). Additive contracts: `SetupDto.Score` (the numeric 0–100 was ALREADY carried
  SetupScorer→SetupConfirmation→Setup; just surfaced via `SetupDtoMapper`, NOT a `DeterministicId` input),
  `RankedSignalDto(int Rank, int Score, SetupDto Setup)` (slice-6 extends with take-state via APPENDED optional params),
  `GetSignalsQuery`, `SignalsUpdated`. `SetupConfirmedSignalFeedHandler` (add→rank→publish) + `GetSignalsQueryHandler`;
  `GET /api/signals?symbol=&style=&grade=&max=`; `TradingHub.SignalsUpdated` + `SignalsUpdatedBroadcaster`.
- **Slice 6 — DONE & GATED (895 unit +30, 23 arch, 44 integration non-Docker, 0 warnings, format clean;
  defensive-guardrail-auditor 7/7 PASS, ict-domain-expert SHIP/CONFORMANT 5/5).** Manual "Take" semi-auto paper flow.
  `TradeEntryMode { Auto, Manual }` (orthogonal to the limit-vs-immediate `EntryMode`); `PaperTradingOptions.DefaultEntryMode`
  POCO default **Auto** (keeps code-constructed-options tests byte-identical) + `appsettings.json` default **Manual** (the
  running product gives the Take workflow); per-instrument `InstrumentOptionOverrides.EntryMode` + `EffectiveEntryMode`.
  **One extracted internal `SetupTradeOpener`** is the SOLE simulated-open path — both `SetupConfirmedHandler` (Auto) and
  `TakeSetupCommandHandler` (Take) call it (byte-identical sizing, proven + a reflection guardrail test asserts no
  order/broker/execute symbol). In-memory `PendingOpportunityStore` (bounded, id-keyed, expiry by age +
  killzone-end reusing the SAME `KillzoneClock.IsActiveEntry` "don't-chase" math). `TakeSetupCommand` → rehydrate →
  `SetupTradeOpener`, triple-idempotent on the deterministic id. `POST /api/signals/{setupId:guid}/take` (200 opened DTO /
  202 armed / 404 / 409). Final wire shapes for the frontend: `RankedSignalDto(Rank, Score, Setup, EntryMode, IsTaken,
  BlockReason, ExpiresAtUtc)` + `InstrumentSettingsDto.EntryMode` (3-state). **CONVENTION:** Manual mode is a LIVE-operator
  workflow — NOT meaningful in replay, so the `BacktestEngine` stays Auto. Minor deferred: an expired take currently maps to
  404 (not a distinct 409-Expired) — cosmetic.
- **Slice 8 — DONE & VERIFIED (149 vitest +28, build+lint clean).** Frontend Signals view + manual Take. `types/api.ts`
  mirrors the final wire: `SetupDto.score`, `RankedSignalDto` (WRAPPER: rank/score/setup/entryMode/isTaken/blockReason/
  expiresAtUtc), `InstrumentSettingsDto.entryMode`. `SignalsPage` + `SignalsFeed` + `ScoreBar` (ranked, filterable
  symbol/style/grade/tf/min-RR/hide-taken; row click focuses the chart switching symbol+TF+style); `TopSignalsPanel` on the
  Live right rail. `useSignals` + `useTakeSignal` (200 opened DTO → merge active-trades + `notifyTradeOpened`; 202 armed;
  404/409 surface `{error}`); SignalR `SignalsUpdated` → signals cache + new-id → `notifyNewOpportunity` toast (wires slice
  7's reserved hook). Take button is "Take (paper)" / aria-label `Take paper trade on ${symbol}` — no execute/buy/sell
  verb (guardrail assertion green). Settings Discovery Auto/Manual is now a LIVE 3-state per-instrument control bound to
  `InstrumentSettingsDto.entryMode`. `/signals` route + nav.
- **FULL BRANCH VERIFIED TOGETHER: backend 895 unit + 23 arch (0 warnings, format clean) + frontend 149 vitest (build+lint
  clean).** Branch `feature/slice1-instrument-profiles-silver-bullet`: 69 files changed, +3352/−725 vs main, UNCOMMITTED.
- **Slice 10 — DONE.** Optimizer retune over the fetched `data/*.csv` (7 instruments). **No new bakes warranted** — every
  net-positive ≥15-trade winner is ALREADY the live default (NAS100 M5 k=6, USDJPY M5 drop-FvgPresent; EURUSD/GBPUSD/AUDUSD
  strict). **Gold's FIRST valid run:** the new metal sizing works (11–27 trades vs 0 before) but it's net-negative in every
  config → NOT baked (flagged for a future draw/cost model or native-tick data). **Setups/week:** net-profitable basket
  (EURUSD M15 + NAS100 M5 + USDJPY M5) ≈ **2.83/wk**; discovery-mode recall ceiling (M5 + k=5 across all 7) ≈ **10.4/wk**
  but the FX legs net-lose at k=5 → breadth+lower-TF raises COUNT, not by gutting confluence (confirms the research). Data
  caveat: M15 = robust 8-yr (2018→2026); M5 = ~2.7-yr. Findings appended to `docs/ict-frequency-research-2026.md`
  (`## Retune results (2026-06)`). No code/appsettings change from the retune.

**🏁 PRODUCT-OVERHAUL PROGRAM COMPLETE — all 10 slices done, FULL suite green: backend 895 unit + 23 arch (0 warnings,
`dotnet format` clean) · integration 79 (+1 skip, Testcontainers/Postgres) · E2E 12 (Reqnroll/Testcontainers) · frontend
149 vitest (build+lint clean).** Branch `feature/slice1-instrument-profiles-silver-bullet`, ~71 files, UNCOMMITTED
("commit only when asked"). **Two real bugs were caught by running the Docker-gated suites (which the slice agents could
not run) and FIXED:** (1) `CandleRepository.AppendAsync` built EF placeholders as `{@p0}` — EF's `ExecuteSqlRawAsync` wants
POSITIONAL `{0}` (the `{`-before-`@` threw `FormatException`); candle persistence was fully broken — fixed to `{0}` + a
`timestamptz` CAST; (2) `TakeSignalEndpointTests` used a 2024-dated setup against the live wall clock → the pending was
age/killzone-pruned before the take (404) — fixed to a current-dated setup + `Pending:ExpireOnKillzoneEnd=false` (the live
Manual flow is sound; the test was time-fragile, per the ict-domain-expert's S1).

**CONVENTION (added):** the Testcontainers integration + E2E suites RUN under the DEFAULT sandbox (Docker is reachable
there; the disable-wrapper breaks the test-host IPC) — and they catch DB/SQL bugs that pure-unit + arch gates cannot, so
RUN THEM before declaring a persistence/endpoint slice done (the slice agents lack Docker). **NEXT (when the operator asks):**
optional full pr-reviewer over the branch; commit/push/PR per git-workflow; live render-verify (screenshot) the Signals page
+ Take + dismissible notifs + redesigned Settings against the booted Host; optional Turtle Soup detector + a US30/gold data
fetch + gold cost-model revisit.

**🌙 OVERNIGHT IMPROVEMENT LOOP (operator: "add a pessimistic judge, backtest+criticize everything, update memory, do it
again to make it much better — until morning"). Branch `feature/#183-product-overhaul` (PR #184).** A
backtest→pessimistic-judge→fix→memory loop driving the system better each round; changes committed LOCALLY per round (NOT
pushed without the operator). New **`pessimistic-judge` skill** (`.claude/skills/pessimistic-judge`): guilty-until-proven-
innocent adversarial review (look-ahead/phantom fills, guardrail, ICT fidelity, money, overfit, determinism, config-binder,
test gaps); treats implausibly-good results as BUGS and BLOCKs on look-ahead/guardrail/money.

- **🐞 CRITICAL BUG FOUND (the operator's "22 trades in 3yr" complaint led here).** The scanner DETECTS plenty of setups
  (NAS100 M5 k6 = 281) but only ~22 become trades (8% conversion) because the §2.5 entry is a RESTING LIMIT at the OTE/FVG
  and price often never retraces there (faithful — ICT: "no fill is a valid outcome"). The non-default
  **`EntryMode.Immediate` (`Ict:Execution:Entry:Mode=Immediate`) has a LOOK-AHEAD/phantom-fill BUG**: `TradeOrchestrator`
  (line 81-82) opens at `plan.Entry` (the OTE) on the confirmation bar even though the market hasn't retraced there →
  phantom favorable fills → IMPLAUSIBLE results (NAS100 PF **37**, USDJPY PF **88**, +800%). **NOT edge — disregard any
  Immediate-mode numbers.** Default is **Armed** (the correct resting limit, no look-ahead) so the committed system + PR #184
  are UNAFFECTED; Immediate is an off-by-default trap. **Round 2 fixes it = the real frequency lever** (open at the actual
  next-bar price + recompute stop/target/RR = a correct, honest market-on-confirmation entry).
- **Round 1 — DONE & COMMITTED (903 unit, 0 warnings, format clean).** Added opt-in `EntryFillZone {Ote(default),
  ConsequentEncroachment, FvgNearEdge}` on the OTE resolver (`Ict:Detection:Ote:FillZone`) + `FairValueGap.NearEdge`.
  **Empirical judge verdict (kept honest): a NO-HELP.** CE = the FVG midpoint the resolver ALREADY uses (a no-op);
  FvgNearEdge barely moved conversion (8%→7% NAS100, 56%→56% EURUSD M5) and WORSENED edge (PF 1.78→1.25, 1.40→0.91). So
  **entry DEPTH is not the bottleneck** — price often never retraces into the gap at all. The zones stay as harmless opt-ins
  (default Ote = byte-identical). **The honest levers for more trades: (a) breadth + lower TF (M1/M3), (b) the correct
  market-on-confirmation entry (round 2), NOT entry-depth tricks.** CONVENTION: backtest realism — verify a "more trades"
  lever ACTUALLY raises filled trades AND holds edge before trusting it; the judge re-derives suspicious metrics.
- **Round 2 — DONE & COMMITTED.** Definitive entry-fill/frequency answer in `docs/ict-frequency-research-2026.md` §8. The
  big fix (correct market-on-confirmation entry) is **GATED ON OPERATOR APPROVAL** (they explicitly chose FVG-edge over it),
  so the loop does NOT build it autonomously — it's the strong recommendation for the morning.
- **Round 3 — DONE & COMMITTED (904 unit, 0 warnings, format clean).** Take-status correctness: an EXPIRED take wrongly
  returned 404 (the store pruned the stale pending before the lookup). `PendingOpportunityStore.TryTake(id, now, out
  TakeMiss)` captures the reason BEFORE pruning (present-but-stale → Expired, absent → NotFound); `POST
  /api/signals/{id}/take` maps Expired + AlreadyTaken → **409**, unknown → **404** (ict-domain-expert S2). Loop continues on
  SAFE gated items; market-on-confirmation awaits approval.
- **Round 4 — DONE & COMMITTED (905 unit, 0 warnings, format clean).** Ranked-feed strict determinism: two signals tying
  on every rank key had a non-deterministic relative order (the ranker's stable chain fell back to store-enumeration
  order). `SignalRankingService` now pre-orders the ranker input by the deterministic `SetupDto.Id` → the feed + its
  SignalR push are reproducible. (pr-reviewer nit; no struct change.)
- **🌅 LOOP CONVERGED on safe items (rounds 1–4 done; 6 local commits on `feature/#183-product-overhaul`, NOT pushed).**
  The remaining backlog is either ENRICHMENTS (candle↔trade timeframe guard, symbol-scoped repo queries — real but minor,
  better done with operator context) or the **headline frequency fix (correct market-on-confirmation entry) which is GATED
  ON OPERATOR APPROVAL** (they explicitly chose FVG-edge over it). Per the loop's stop rule (safe backlog dry → summarize),
  the autonomous loop winds down here. **MORNING TO-DO for the operator:** (1) approve the correct market-on-confirmation
  entry (the one real lever for more trades — see `docs/ict-frequency-research-2026.md` §8); (2) push the branch / update PR
  #184 when ready; (3) optionally let the loop resume on the enrichment backlog.

**🌅 "Go balanced" session (operator chose the balanced model + asked for a backtest, a best-pairs guide, and the two
fixes). Committed on `feature/#183-product-overhaul`, since PUSHED + MERGED (PR #184 → `main`); OANDA reachable this session.**
- **🐞 CRITICAL FIX — Daily Risk Guard deadlock (committed `4f0e146`, 907 unit green).** The guard halted on the LIFETIME
  consecutive-loss streak (clears only on a win) → after 3 losses it blocked EVERY entry forever (no entry → no win →
  permanent halt). A guard-ON backtest exposed it (EURUSD M15 15→5, NAS100 M5 22→8). Fix: `DailyRiskGuard` Rung 1 now
  ALSO requires the NY DAY to be in the red → a per-day circuit breaker that RESETS each day (fresh day always admits the
  first entry). Confirmed: guard-ON now matches guard-OFF at low frequency. The daily-loss-cap rung was already day-scoped.
- **Data fetched (OANDA practice, token from the User env scope via PowerShell):** NZD/USD + USD/CAD at M15 and M5
  (250k candles each, → `data/`, gitignored). The one-shot fetcher writes to `src/IctTrader.Host/data/` — MOVE the CSVs to
  the repo `data/` after fetching.
- **Full-history sweep (pair × M5/M15 × k, fixed guard ON) → the PROFITABLE setups (= `docs/ict-best-setups-guide.md`):**
  NAS100 M5 6-of-8 (PF 1.78, +$633) · USD/JPY M5 drop-FVG (PF 2.40, +$462) · EUR/USD **M5** strict (PF 1.64, +$188, more
  active than M15) · EUR/USD M15 strict (PF 1.97, +$14) · NZD/USD M15 7-of-8 (PF 1.88, +$114). **Net-negative → drop:**
  GBP/USD (flat), AUD/USD, **USD/CAD (override REMOVED `…`)**, gold, SPX500 (sparse). **Aggregate ≈ 0.5 trades/week** — the
  honest balanced cadence (profitable but selective; faster/looser loses the edge).
- **Decisions:** (a) DONE — USD/CAD override dropped from `Ict:Instruments` (net-neg full history); kept NAS100/USDJPY/NZDUSD
  bakes + EUR/USD strict. (b) DONE — NZD/USD + USD/CAD data fetched; USD/CAD still net-neg, NZD/USD M15 confirmed +.
  Market-on-confirmation: the operator REJECTED it ("not entering fast") after seeing it's the quality-sacrificing extreme;
  the half-built partial was REVERTED. **CONVENTION:** the OANDA one-shot fetcher writes to the Host-relative `data/`, not
  `Ict:Backtest:DataDirectory` — move the CSVs. Re-run `scratchpad`/the sweep on refreshed data to retune (samples 10–60 trades).

**CONVENTIONS reaffirmed/added this session:** (a) "default to live" is satisfied by the multi-granularity capability +
the operator's env-var run, NOT by flipping the committed `Provider` (keeps tokenless CI/dev boots + Host-booting tests
green). (b) Backend slices serialize their `dotnet build` (DLL-lock); parallelize only backend↔frontend (different
toolchains). (c) The manual "Take" (slice 6) MUST reuse a single extracted `SetupTradeOpener` (the one paper-only path)
and add no executor symbol. (d) Bake per-pair tuning only on full-history ≥~15-trade samples picked by NET P&L (restated).

**🏁 "Push, merge, run it, show the winner signal" session (operator's `feature/#183-product-overhaul` → PR #184 → MERGED
to `main`). The product overhaul is shipped and PROVEN live.**
- **Winner-signal hero card (committed `af77fa4`, frontend gates green — 162 vitest, tsc build + eslint clean).** A
  prominent full-width **⭐ TOP SIGNAL #1** banner at the top of BOTH the Live page and the Signals page, featuring the #1
  ranked opportunity (`useSignals().data?.[0]`): symbol + direction + grade chip + score bar + entry/stop/targets + RR +
  the one-line §2.5 reason + the single paper-only action (the shared `TakeControl` → "Take (paper)", which mirrors the
  feed's Auto/blocked/taken/pending states). `web/ict-dashboard/src/components/WinnerSignalCard.tsx` (+ test); wired into
  `Dashboard.tsx` (Live) + `SignalsPage.tsx`; `.winner-card` styles in `index.css`. Paper-only (no execute/order control;
  Dashboard no-execute test still green).
- **PR #184 PUSHED + MERGED** (12 commits → `main` via merge commit `b6e0db0`); local `main` fast-forwarded. The whole
  product overhaul (multi-TF feed, persistence, full-matrix scanning, signals ranking + feed, manual Take, dismissible
  notifications, Live/Settings UX, the hero) is now on `main`. (Merged with CodeRabbit's advisory review still PENDING per
  the operator's standing "merge and continue".)
- **PROVEN LIVE end-to-end on real EUR/USD (screenshots `winner-signal-takeable.png` = the faithful run, +
  `winner-signal-live.png`).** Booted the Release Host on real Postgres (port 55432, both migrations applied incl. the new
  `AddCandles`) driving the `data/EURUSD-M15-live.csv` Replay (62k M5… **M15** bars, 2024→2026); the dashboard built
  `VITE_USE_MOCKS=false` → `wwwroot`, served **single-origin** at `:5080`. Result: the Scanning FSM confirmed a **Grade-A**
  EURUSD Swing setup (score 91, RR 3.0, sweep→MSS→FVG→OB→OTE→draw + 08:30-macro + clean-displacement confluence) and it
  rendered as the hero with a **clickable "Take (paper)"** button; health chip **LIVE** (green), account clean ($10k, 0
  open trades — Manual default opened nothing automatically). **The operator can SEE and TAKE the winner signal.**
- **⚠️ RUN-ENVIRONMENT LEARNINGS (load-bearing for the next live run — these bit this session):**
  1. **Run the Host with the project as ContentRoot, or `appsettings.json` + `wwwroot` DON'T load.** `dotnet
     bin/Release/net10.0/IctTrader.Host.dll` defaults ContentRoot to the CWD; from the repo root it looks for
     `<repo>/appsettings.json` (absent) → the app runs on POCO defaults (e.g. `DefaultEntryMode` falls back to **Auto**, NOT
     the committed **Manual**; per-pair bakes don't apply) and `MapFallbackToFile` 404s the SPA. **Fix:** pass
     `--contentRoot "<repo>/src/IctTrader.Host"` (no `cd` needed) — then appsettings (Manual + bakes) AND single-origin
     `wwwroot` both resolve. Single-origin ALSO fixes the SignalR "DEGRADED" health chip (the Vite `:5173` ws-proxy
     negotiate is flaky; same-origin `:5080` connects clean).
  2. **Full-matrix scanning (WS-C) means the active STYLE's entry-TF must match the fixture's TF.** A replay of **M15**
     data with the default `[Intraday]` (entry TF = **M5**) scans NOTHING (`StyleTimeframeMap.StylesFor(M15,[Intraday])=∅`)
     → 0 setups. For an M15 fixture set `Ict__Scanning__ActiveStyles__0=Swing` (Swing→M15). (M5 fixture → Intraday.)
  3. **A HISTORICAL replay's setups are time-skewed vs the wall clock**, so the recency/expiry windows prune them. For a
     demo on old data, widen `Ict__Signals__RecencyCutoffMinutes` (else the signals feed is empty) AND
     `Ict__PaperTrading__Pending__MaxPendingMinutes` + set `Ict__PaperTrading__Pending__ExpireOnKillzoneEnd=false` (else
     every signal shows `blockReason=Expired` and the Take button is disabled). These are DEMO overrides only — live data
     is current-dated, so the committed 240-min defaults are correct in production.
  4. The deterministic `SetupDto.Id` means re-replaying the SAME fixture re-confirms the SAME ids; if a prior run
     auto-opened trades (Auto), those ids read `isTaken/AlreadyTaken` on the next run — **TRUNCATE `paper_trades,
     armed_entries, paper_accounts`** (via `docker exec icttrader-postgres psql`) for a clean takeable demo.
  5. `dotnet test` runs under the DEFAULT sandbox; the Host/curl/Postgres/OANDA all need `dangerouslyDisableSandbox:true`
     (restated — still true). The Vite dev server + the Host both launch as a `run_in_background` task's MAIN command.

**🟢 LIVE OANDA feed — ALL assets × ALL timeframes (operator: "no csv anymore (just for backtest), live data on all
asset on all timeframe"). RUNNING + PROVEN.** Switched the running Host from CSV Replay to the live OANDA practice feed
via env overrides only (committed `Provider` stays `Replay` for CI). A `live-feed-config-recipe` workflow mapped the
exact config (feed options, the 14-asset catalog, granularity allowlist, scan/chart per-TF, guardrail) before booting.
- **Feed config (env only; token from the Windows User env var `Ict__MarketData__Oanda__Token`):**
  `Ict__MarketData__Provider=Oanda` · `Oanda__LiveStreaming=true` · **`Oanda__Instruments__0..13`** = the 14 catalog
  instruments in OANDA underscore form (EUR_USD, GBP_USD, USD_JPY, AUD_USD, USD_CHF, USD_CAD, NZD_USD, EUR_GBP, EUR_JPY,
  GBP_JPY, NAS100_USD, SPX500_USD, US30_USD, XAU_USD) · **`Oanda__Granularities__0..6`** = **M1,M5,M15,M30,H1,H4,D** (use
  `D` not `D1` — the allowlist is `M1,M5,M15,M30,H1,H4,D,W`; parser maps D→D1; `W` omitted as poll-waste) ·
  `Oanda__MaxConcurrentFetchesPerPoll=6` · `Oanda__HistoryCount=1000` · `Oanda__PollSeconds=60` ·
  **`Ict__MarketData__Persistence__Enabled=true`** (batched candle dual-write → time-range chart) ·
  **`Ict__Scanning__ActiveStyles__0..3 = Scalp,Intraday,Swing,Position`** (so all four scanned entry-TFs M1/M5/M15/H4 run)
  · `Ict__PaperTrading__DefaultEntryMode=Manual` · `ConnectionStrings__PaperTrading=…` (REQUIRED — ingestion persists) ·
  `ASPNETCORE_URLS=http://localhost:5080` · launch the **built Release DLL** with `--contentRoot <repo>/src/IctTrader.Host`.
- **Proven (06-30):** provider Oanda, all 14 symbols + 4 styles in `/api/config`; OANDA returns live candles (EURUSD M5
  last = now); the chart matrix shows **today's** candles for every sampled asset across M1/M5/M15/H4 (+ H1/H4/D1); the
  scanner confirmed setups on LIVE data across assets/styles/TFs incl. **Grade-A NAS100USD Intraday** + **Grade-A GBPJPY
  Swing**; `/api/signals` ranked live across USDJPY(Scalp M1)/XAUUSD(Scalp M1)/EURUSD(Swing M15)/NAS100(Intraday M5), all
  **Manual + takeable**; dashboard health **LIVE** (SignalR same-origin), **OANDA** badge on Account & Config. Screenshots
  `live-oanda-dashboard.png` + `live-oanda-gbpjpy-h4.png`.
- **Chart now covers ALL live assets/TFs (committed `90e09f1`):** `ChartPanel.tsx` `SYMBOLS` → all 14 catalog instruments
  (NAS100/SPX500/US30 + Gold labels), `TIMEFRAMES` → M1/M5/M15/M30/H1/H4/D1. Proven by charting GBPJPY on H4 live. FE
  gates green (tsc + build + 162 vitest; the 3 ChartPanel `react-refresh` lint warnings are pre-existing).
- **OANDA mechanics learned this run:** (a) the multi-series feed **backfills ALL `instruments × granularities`
  (14×7=98 series, HistoryCount each) FIRST, then starts the live poll** — so until backfill completes a series shows its
  backfill-end time (or CSV-fallback for an as-yet-unfilled ring), advancing to "now" once the poll begins. (b) Candle
  **persistence is a bounded channel (BatchSize 200, DropOldest)** — the heavy backfill burst overflows it, so the DB lags
  during backfill (the in-memory ring is the live truth); steady-state poll trickle persists fine. (c) Transient OANDA
  edge failures (`000`/SocketException 10054) are normal (~1 in 5) and handled by the feed's bounded backfill retry +
  per-series live skip — a healthy series keeps streaming. (d) **98 GETs/poll** at PollSeconds=60 is within practice
  limits; trim instruments/granularities or raise PollSeconds if 429s appear. **Guardrail: read-only by shape — only
  `/v3/instruments/{i}/candles` GETs, practice host, no order path (verified by the recipe workflow).**
- **NOTE — chart selector expansion is committed on PR #185 but NOT yet built into a redeployed `wwwroot` by default;**
  the running demo serves the rebuilt SPA from `src/IctTrader.Host/wwwroot` (gitignored). After pulling, rebuild the SPA
  (`VITE_USE_MOCKS=false npm run build` → copy `dist/*`→`wwwroot`) to serve it single-origin.

**🩹 Live-UI fixes — rail/chart/entry-marker (operator feedback; PR #186 → merged `fc3a4a8`).** Render-verified against
the booted Host on live OANDA. 162 vitest + tsc + build green (3 pre-existing ChartPanel `react-refresh` lint warnings):
- **Right rail was cramped / left Alerts too wide.** `index.css`: narrowed Alerts (`minmax(240,300)`), widened the rail
  (`minmax(360,520)`); added **`.layout__trades > * { flex-shrink: 0 }`** so the rail SCROLLS instead of squishing cards
  into overlapping slivers (that was why Top Signals/Active Trades were invisible). `Dashboard.tsx`: reordered the rail to
  **Market Status → Top Signals → Active Paper Trades → Performance → Account&Config** (actionable first; the bulky
  LiveConfig card last).
- **Chart clutter.** `api/client.ts` `fetchOverlays` drew EVERY recent setup's entry/stop/T1/T2/draw lines on a TF (a
  15-line ladder). Now draws only the **most-recent setup** per timeframe (`MAX_CHART_SETUPS = 1`, sorted by
  `detectedAtUtc`). `client.test.ts` updated to assert most-recent-only.
- **Entry arrow misplaced.** It used a bar-relative marker (`aboveBar`/`belowBar`) → floated at the candle wick, ~pips
  off the entry. Now anchored to the entry PRICE via lightweight-charts v5 **`position:'atPriceMiddle', price:o.entry`**
  (`IctChart.tsx`); `IctChart.test.tsx` asserts `atPriceMiddle` + `price === entry`. **CONVENTION: price-level chart
  markers use `atPriceMiddle`+`price` (v5 SeriesMarkerPrice), never the bar-relative position.**

**🤖 Auto-mode runtime switch + loss investigation (operator-driven; runtime only, NOT committed).** The operator asked
to run all signals Auto, then to investigate the losses:
- **Auto mode** = `Ict__PaperTrading__DefaultEntryMode=Auto` env override on the running Host (committed default stays
  Manual). Every confirmed setup auto-opens a paper trade (no Take); self-limits at the ~5% open-risk cap; Daily Risk
  Guard halts after a bad day if enabled. Notifications: in Auto the "New signal" + "Trade opened" toasts fire together
  (the redundancy the operator flagged — offered to suppress the entry toast in Auto, not yet done).
- **Loss investigation (7 trades, all StopHit, net −$153.80, 29% win):** NOT a bug — risk control held (every loser
  exactly −1R). Root causes: (a) **5 of 7 were M1 Scalp** (noisiest TF); (b) **2 same-bar straddles** (limit filled THEN
  stopped within the same candle → conservative −1R) = −$92, 60% of the loss; (c) **0/7 hit target** — the 2 wins were
  trailed-stop partials (+0.7–0.9R) → negative expectancy; (d) costs $23 (15%); (e) **7-trade sample = variance, not
  signal**. NONE were the validated profitable configs.
- **Remedy (relaunched, clean DB):** Auto with **`ActiveStyles=[Intraday,Swing]` only** (Scalp/Position dropped) — scans
  M5+M15 with the baked per-pair tunings, drops the M1 noise. All 14 instruments × 7 granularities still STREAM (chart
  shows every TF); only SCANNING is restricted. Verified: alerts/setups are Intraday+Swing only, no Scalp.
- **INSIGHT — the backfill is a mini-backtest on EVERY launch:** `HistoryCount=1000` re-feeds the last ~83h and setup ids
  are deterministic, so Auto **re-confirms + re-trades the same recent-history setups each relaunch** (the −$77 NAS100 M5
  same-bar straddle kept reappearing). Genuinely-new trades come from the live poll going forward. For a clean
  forward-only run, lower `HistoryCount`. **Process-name note:** the Host runs as `dotnet.exe` (not `IctTrader.Host.exe`)
  when launched via `dotnet <dll>` — `Get-Process IctTrader.Host` shows 0 even while it serves; check `curl :5080/api/config` instead.

**🔑 `.env`-based OANDA credential + live-run helper (PR #187 → merged `380f53f`).** The OANDA token moved from an ad-hoc
Windows User env var into a standard **gitignored `.env`** (token + the full live-feed config). Committed: **`.env.example`**
(template, placeholder token), a **`!.env.example`** negation in `.gitignore` (so the template commits while `.env`/`.env.*`
stay ignored — verified via `git check-ignore .env`), and **`run-live.sh`** (sources `.env`, fails fast on missing/placeholder
token or unbuilt DLL, boots the Host single-origin). **CONVENTIONS:** (a) the OANDA credential lives in `.env` (never
committed); the live run is `bash run-live.sh` (no more long export block). (b) Values with `;`/spaces (the Postgres
connstring) MUST be QUOTED in `.env` or `source` splits them. (c) `.env` defaults to the **Intraday+Swing / Auto** config
settled on above; edit it to change the live config (no rebuild). The committed `appsettings` default stays Manual + all-styles + Replay for CI.

**🐳 Dockerized — one-command stack (PR #189 → merged `ee20f76`).** `docker compose up -d --build` now builds + runs the
WHOLE app (Postgres + the .NET Host serving the SPA single-origin at **http://localhost:5080**) with no manual steps.
- **`Dockerfile`** (multi-stage): (1) **`node:24-bookworm-slim`** builds the React SPA in live mode (`VITE_USE_MOCKS=false
  npm run build`); (2) **`mcr.microsoft.com/dotnet/sdk:10.0`** `dotnet publish`es the Host (+ all module projects); (3)
  **`aspnet:10.0`** runtime = `/publish` + the SPA in `wwwroot`, `ENTRYPOINT dotnet IctTrader.Host.dll` on :5080 (ContentRoot
  defaults to the `/app` WORKDIR, so appsettings.json + wwwroot resolve — no `--contentRoot` needed in-container).
  **BUILD GOTCHA (solved):** the committed `package-lock.json` was generated on Windows and OMITS the Linux platform
  optional deps (`@emnapi/*`, the linux rollup binary), so **`npm ci` fails in a Linux container ("Missing … from lock
  file", EUSAGE) regardless of npm version** — the web stage uses **`npm install`** (resolves the right platform deps
  cross-platform) on the glibc `slim` image (not `alpine`/musl, which needs the `-musl` native variants).
- **`docker-compose.yml`** gained an **`app`** service: `build: .`, `depends_on: postgres (service_healthy)`,
  **`env_file: [{path: .env, required:false}]`** (reads the OANDA token + live config when `.env` is present; boots on
  Replay defaults otherwise), an `environment:` block that OVERRIDES the in-network connstring (**`Host=postgres;Port=5432`**,
  NOT localhost:55432), `Ict__Database__AutoMigrate=true`, `ASPNETCORE_URLS=http://+:5080`, and `Ict__Backtest__DataDirectory=
  /app/data` with `./data:/app/data:ro` mounted for the Backtest/Optimizer. Postgres keeps host port **55432** + the
  `icttrader-pgdata` volume. (`environment:` wins over `env_file:`, so the .env's localhost connstring/URL are safely overridden.)
- **NEW — guarded startup auto-migrate (`Program.cs`):** `if (Ict:Database:AutoMigrate)` migrates BOTH write-model contexts
  (`PaperTradingDbContext` + `MarketDataDbContext`, same DB) on boot via `.Database.Migrate()`. **Default OFF** (appsettings
  `Ict:Database:AutoMigrate=false`) so CI/tests/local-dev keep applying migrations explicitly; compose turns it ON so a fresh
  Postgres self-schemas — no `dotnet ef database update` step in the container. Both contexts share one `__EFMigrationsHistory`
  (the manual two-context flow already proved this coexists).
- **`.dockerignore`** keeps the 534M `data/` CSVs + `node_modules`/`bin`/`obj`/`.git`/`.env`/screenshots out of the build context.
- **VERIFIED live:** image builds clean; Postgres healthy; the app auto-migrated a fresh DB (`paper_accounts`/`paper_trades`/
  `armed_entries`/`candles` + `__EFMigrationsHistory` created); `/api/config` → provider=Oanda + the `.env` Intraday+Swing/14-symbol
  config; SPA serves 200 at :5080. Build 0 warnings · 907 unit tests green. Read-only/paper-only — no order path. **CONVENTIONS:**
  (a) run the whole app with `docker compose up -d --build` (the Host non-docker run via `bash run-live.sh` still works);
  (b) the Docker app OWNS :5080 — stop any non-docker Host first; (c) Docker Desktop's Linux engine can drop mid-build on this
  box (the `dockerDesktopLinuxEngine` pipe vanishes) — wait for it to recover + re-run `docker compose up -d --build`.
