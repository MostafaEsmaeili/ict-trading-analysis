# ICT Automated Trading-Analysis System — Implementation Plan

> **Status:** Planning. This file is the executable blueprint for a fleet of implementation agents.
> **Defensive posture:** This system is **analysis + paper-trading ONLY**. It must NEVER place a live order with real capital. Live execution is made *structurally impossible* (no broker/order interface exists anywhere) — not merely disabled by a flag.

---

## 1. Context — why we are building this

The repository `c:\Repos\Personal\ICT transcribe` currently contains only **ICT (Inner Circle Trader) course transcripts** — 24 episodes of the *Market Maker Primer Course* and 41 episodes of the *2022 Mentorship* (already cleaned into `.txt` by `build_transcripts.py`). There is **no code yet** (greenfield, not a git repo).

The goal is to translate the ICT methodology described in those transcripts into a **comprehensive, defensive, automated trading-analysis system** that:

1. Continuously scans incoming tick/candle data for setups that strictly match the ICT rules.
2. Raises an **alert** (UI + console) with the **exact human-readable reasoning** when a high-probability confluence forms (e.g., *"Bullish FVG formed inside London Killzone after Asian High sweep, MSS confirmed, OTE 0.705"*).
3. Simulates the trade internally (**paper trading**): entry, stop, target(s), time-in-trade — and tracks **win rate, R:R, drawdown, equity curve** to prove strategy performance over time.
4. Connects to a **free Forex data provider** (OANDA practice / Finnhub / TraderMade) and is architected for a future **MetaTrader 5 bridge** (read-only market data over TCP/sockets).

**Tech stack (fixed by requirement):** .NET 10 C# Web API · **Modular Monolith** with feature modules decoupled behind an **in-memory message bus** (NO MediatR — it is now commercially licensed; we use a thin, swappable `IMessageBus`) · Domain-Driven Design core · PostgreSQL + EF Core (JSONB) · SignalR real-time · React + TypeScript dashboard · E2E tests with Gherkin (Reqnroll) + Testcontainers + xUnit. **Clean Code throughout: no magic numbers (→ strongly-typed Options) and no magic strings (→ `.resx` resource files).**

**This plan is structured for parallel agent execution** — see §11 for the dependency-ordered work-package table and frozen contracts.

> **NOTE TO IMPLEMENTERS — house style (self-contained; do NOT depend on any sibling repo):** minimal-API hosting; **Clean Code + SOLID**; strict Options pattern (no magic numbers — every ICT/trading constant lives in `appsettings` bound to validated POCOs); **no magic strings — all human-facing/log/alert/validation/reason text lives in `.resx` resource files** (localization-ready); enum-driven provider selection with a strategy-factory ("new provider = new impl + one DI line"); resilience via decorators; `Directory.Build.props` with `<Deterministic>true</Deterministic>` + nullable enabled + warnings-as-errors. Architecture = **modular monolith** (see §3.0a): feature modules are decoupled behind an in-memory message bus and can be extracted to separate services later without touching module code.

---

## 2. ICT Domain Logic Extraction (summary of rules being translated to code)

> §2.1–2.4 are the **framework** from the Market Maker Primer (Killzones, Asian Range, Judas Swing, AMD, FVG, Order Blocks, Liquidity Sweeps, MSS, OTE, Daily/Weekly Bias, SMT, Risk), extracted with episode citations.
>
> **§2.5 is THE entry model** — the concrete, mechanical setup the scanner hunts — **mined from the 41-episode 2022 Mentorship (the main course)** and adversarially verified. It is the source of truth for `RequiredConditions`, confluence weights (§4.4), and the detector set (§4.2). Implementers must read §2.5 (esp. the §2.5.7 fidelity caveats) before coding any parameter.

### 2.1 Sessions / Killzones — all times **New York (America/New_York), DST-aware**; financial day starts 00:00 NY
| Killzone | Window (NY) | Typical scalp | Notes |
|---|---|---|---|
| Asian | 19:00–00:00 | 15–20 pips (20–50 in crosses) | Forms the **Asian Range** |
| London Open | 02:00–05:00 | 25–50 pips | **Highest probability** of setting the daily high/low |
| New York Open | 07:00–09:00 | 20–30 pips | Majors coupled with DXY |
| London Close | 10:00–12:00 | 10–20 pips | Short-term reversals/continuation |

### 2.2 Time-based structures
- **Asian Range** — high/low between 19:00–00:00 NY; projected forward. Narrow Asian range → high odds of a trending day. Bullish: buy retest of range high, stop below range low. Bearish: mirror. Minimum stop ~10 pips.
- **Judas Swing** — false move after midnight NY (London session). Bullish day: sweep **above** Asian high (10–20 pips) then reverse **below** opening/Asian low → real move up. Bearish day: mirror. Creates the day's high/low.
- **Power of 3 / AMD** — daily candle engineered as **Accumulation → Manipulation (stop raid against the intended direction) → Distribution**. Manipulation drives liquidity to the opposite side before the real expansion.

### 2.3 Pattern primitives (fractal — all timeframes)
- **Swing High** = candle high with 2 lower highs either side; **Swing Low** = low with 2 higher lows either side.
- **Fair Value Gap (FVG)** = 3-candle imbalance. **Bullish** when `candle1.High < candle3.Low`; **Bearish** when `candle1.Low > candle3.High`. Entry on retrace into the gap.
- **Liquidity** — buy-side above old/equal highs, sell-side below old/equal lows. **Sweep/stop-raid** = price trades through the level then reverses (wick beyond, close back inside).
- **Order Block** — last down-close candle(s) before an up move = **bullish OB**; last up-close before a down move = **bearish OB**. Key level = the block's **opening price**.
- **Displacement** — energetic move that creates an FVG; precondition for valid MSS/entry.
- **Market Structure Shift (MSS / CHoCH)** — break of a short-term swing **after** a liquidity sweep, signalling reversal of intraday delivery.

### 2.4 Entries, bias, targets, risk
- **Optimal Trade Entry (OTE)** — Fibonacci retrace of the impulse leg: entry zone **0.62–0.79**, sweet spot **0.705**; stop at the swing extreme; targets at **0 level** (old high/low) and negative extensions **-0.27, -0.62, -1.0** (symmetrical). Minimum **2:1** R:R (often 3:1).
- **Daily Bias** — previous-day high/low as draw-on-liquidity; outside-day-down-close = potential bullish reversal (and mirror).
- **Weekly Bias** — Sunday open as baseline; weekly low tends to form **Tue London → Wed NY open** (bullish week); Wednesday is the "line in the sand". Swing entry: buy ~30 pips below Sunday open, 150-pip stop, 150–300 pip target.
- **SMT Divergence** — two correlated instruments (pair vs DXY, or correlated pairs) on the **same timeframe**: one makes a higher high/lower low while the correlated one fails → divergence confirms reversal.
- **Money Management** — risk **1–2%** per trade (max 3%); after a loss cut to 1%, after a 2nd loss cut to 0.5%, restore after **50% recovery**; after **5 consecutive wins** drop leverage to lowest unit. Avoid martingale. Target ≥ 3:1 R:R model. Confluence target threshold < 10–15 pips. Take partial at +100 pips / first target.

### 2.5 THE Entry Model — mined from the 41-episode 2022 Mentorship (the MAIN course)

> Produced by the `mentorship-entry-model-mine` workflow (40 episodes extracted → synthesized → adversarially verified). This is the concrete, mechanical setup the scanner hunts. Source-episode citations in brackets. **Read the fidelity caveats (§2.5.7) before coding any parameter as a hard rule.**

**Model:** *ICT 2022 Mentorship Intraday FVG Model — Liquidity Sweep → MSS/Displacement → PD-Array OTE Entry.*
**In one line:** In an active killzone and aligned with HTF daily bias, wait for a **liquidity sweep** of a prior high/low, then a **market-structure shift with energetic displacement**, then enter on the retrace into a **discount/premium FVG (or order block)** at the **OTE 62–79%** zone, stop **beyond the swept swing**, targets at **opposing liquidity**.

#### 2.5.1 The mechanical checklist (execution order)
| # | Step | Rule (automatable) | Key params | Eps |
|---|---|---|---|---|
| 1 | **HTF/Daily Bias** | Daily: most-recent MSS + price in discount(<50%)/premium(>50%) of the daily dealing range + draw-on-liquidity direction. <50% ⇒ bullish, >50% ⇒ bearish; confirm with 3+ consecutive directional daily closes. Unclassifiable ⇒ **NEUTRAL (no trade)**. Only trade in the bias direction. | equilibrium = 50% fib of daily range; 3+ closes | 02,07,09,10,11,12,18,19,37,40 |
| 2 | **Draw on Liquidity (target)** | Identify the opposing pool price is drawn to: relative-equal highs/lows, prior-day H/L, prior swing H/L, unfilled HTF FVG, big figures (00/20/50/80). Must exist before the trade is valid. | mark on 15m + daily | 02,03,08,17,18,36,40 |
| 3 | **Active Killzone (time gate)** | Only inside an enabled killzone (NY local). **FX:** NY 07:00–10:00, London Open 02:00–05:00, London Close 10:00–11:00, PM 13:30–16:00. **Index:** AM 08:30–11:00 (last entry ~10:40). **HARD no-trade lunch 12:00–13:00.** News ≥10:00 extends to 11:30. | see §2.5.5 | 02,03,05,08,17,18,20,21,39,40,41 |
| 4 | **Liquidity Sweep (Judas/stop raid)** | Price raids a prior pool **against** trade direction (shorts: sweep buy-side above equal highs / Judas above the midnight or 08:30 open; longs: mirror). Power-3 accumulation→manipulation. Bearish needs price **above** the reference open (premium); bullish **below** (discount). | no fixed pip; clean run beyond level | 02,03,04,06,09,17,19,24,25,39 |
| 5 | **MSS w/ Displacement** | After the sweep, an **energetic** displacement candle breaks a prior short-term swing and **closes meaningfully beyond** it. Weak/wick-only break ⇒ **NO TRADE**. Confirm on 15m, refine 5→4→3→2→1m. Swing = 3-candle. | close beyond swing; body/range filter | 02,03,04,05,06,10,24,25,41 |
| 6 | **PD-Array Entry (FVG/OB)** | Find the **first FVG** stepping 15m→1m within the displacement leg; none down to 1m ⇒ **NO TRADE**. FVG must be in the correct half: shorts ≥50% (premium — *never sell in discount*), longs ≤50%. OB confluence: bullish OB = down-close cluster before up-displacement, entry at OB open +1 tick (+3 pips FX); a valid OB **requires** its FVG. Two stacked FVGs ⇒ enter the better, size to survive a stab into the farther. | 3-candle FVG; 50% split of displacement leg | 02,03,06,09,10,12,13,25,26,33 |
| 7 | **OTE fib refine** | Anchor fib **body-to-body** on the displacement swing (wicks only on FOMC/NFP). Limit order where FVG/OB coincides with **OTE 62–79% (default sweet spot 70.5%)**; Ep41 variant 62–70%. 50% = equilibrium. | OTE 62–79%; eq 50% | 08,19,22,34,38,40,41 |
| 8 | **Stop** | Beyond the protected swing: shorts above swept swing-high / FVG-high / OB-high (1–2 ticks, ~10 pips FX); longs below swept swing-low / OB-low / displacement-low (1 tick / 0.25 pt ES). Stacked FVGs ⇒ clear the farther. No premature breakeven — require **time AND price** (structure broken) to trail. | 1–2 ticks / ~10 pips FX | 02,06,07,13,17,25,31,39,41 |
| 9 | **Targets & management** | Tiered: **T1** (partial) = nearest FVG boundary / 50% equilibrium / first short-term H/L; **T2** (runner) = opposing relative-equal / prior-day H/L / HTF FVG draw. SD extensions −1/−1.5/−2 when no range target. Mgmt: at 50% of T1 range → stop to 25% risk; at 75% → breakeven. Min RR ~2.5R (default). ~80% off by London Close; runners into PM. **Max hold 90–120 min; no overnight.** | see §2.5.5 | 06,08,09,12,17,22,39,40,41 |

#### 2.5.2 RequiredConditions (ALL must be true to confirm → maps to `ConfluenceOptions.RequiredConditions`)
Bias one-sided (not neutral) · inside an enabled killzone (not lunch) · liquidity sweep against direction occurred · energetic displacement broke structure (MSS) · FVG exists in the displacement leg · entry PD-array in correct premium/discount half · valid draw-on-liquidity target giving ≥~2.5R · direction matches HTF bias · not a calendar-blocked day (post-FOMC, NFP Thu/Fri, NFP-week Wed+).

#### 2.5.3 Weighted confluences (→ `SetupScorer` weights, 0–1)
KillzoneEntry **1.0** · LiquiditySweep **0.95** · Displacement/MSS **0.95** · FVGPresent **0.9** · Premium/DiscountHalf **0.85** · BiasAligned **0.85** · OTE 62–79% **0.7** · OrderBlockConfluence **0.65** · DrawTargetRR≥2.5 **0.65** · SMTDivergence **0.55** · OpenPriceReference **0.5** · MacroTime(08:30/09:30/13:30/15:00) **0.45** · CleanPriceAction **0.4** · CalendarDriver **0.35**.

#### 2.5.4 Grading (→ alert gate)
`score = Σ(matched weights)/Σ(applicable weights) ×100`. **A** ≥80 & all required true (high-confidence alert) · **B** 65–79 & all required true (tradeable) · **C** 50–64 (watchlist, no alert) · **Reject** <50 or any required false. **Alert floor = 65** (only A & B fire).

#### 2.5.5 Exact parameters (appsettings `Ict:*`)
- **Killzones (NY):** FX NY 07:00–10:00 · London Open 02:00–05:00 · London Close 10:00–11:00 · PM 13:30–16:00 · Index AM 08:30–11:00 (last entry ~10:40) · **no-trade 12:00–13:00** · news extension →11:30 · macros 08:30/09:30/13:30/15:00–16:00 · midnight 00:00 open reference.
- **Fibs:** equilibrium 50% · OTE 62–79% (sweet spot 70.5%; Ep41 62–70%) · OB mean-threshold 50% · target ext −1/−1.5/−2 SD · body-anchored (wicks on FOMC/NFP).
- **Risk:** developing 0.25–0.5% · default **1%** with-bias · experienced 3–3.5% · hard max 4.5% · loss-ladder 1%→0.5%→0.25% (restore after recovering 50% of loss).
- **RR:** min ~2.5R (examples to >8R). **Stops:** index 1–2 ticks (ES 0.25 pt) / FX ~10 pips beyond swing/OB; OB entry +3 pips spread; far-FVG clearance when stacked. **Sweep:** no fixed pip — must take the level + be followed by energetic displacement + FVG in correct half.

#### 2.5.6 Detector set (→ extends §4.2)
DailyBias · DealingRangeEquilibrium · DrawOnLiquidity · KillzoneClock (DST-aware, instrument-class aware) · LiquiditySweep · MarketStructureShift · Displacement (energy filter) · FairValueGap (top-down first-FVG, stacked-FVG flag, two-touch invalidation) · OrderBlock (requires FVG; opening-price + 50% mean-threshold) · BreakerBlock · OTEFib (body/wick selection) · PremiumDiscountGate · SMTDivergence · PowerThree (midnight/08:30 anchored) · StandardDeviationProjection · StopPlacement · StopManagement (50%→25%, 75%→BE) · TargetLadder · EconomicCalendarFilter · SessionMacro · RiskSizing (mini/micro, loss-ladder) · NeutralCondition (sloppy/HRLR suppressor).

#### 2.5.7 Fidelity caveats from the adversarial verifier (confidence: MEDIUM — resolve before hard-coding)
1. **Provenance to confirm:** the **70.5% sweet spot** and **Silver Bullet (10–11 NY)** were *not* explicitly grounded in these 2022 episodes (they're ICT canon from the Primer/general teaching). Tag them as Primer-sourced defaults, not Mentorship-derived; keep configurable.
2. **Single canonical OTE:** resolve **62–79% vs Ep41's 62–70%** — default to 62–79% (sweet spot 70.5%), expose the band in config; don't present both as one fact.
3. **Instrument-class killzone split:** the synthesis **mixed FX and index-futures windows**. Implement an **instrument-class switch** (FX vs index) so `KillzoneClock` uses the right windows; this project defaults to **FX**.
4. **`min RR ~2.5R` is a default, not a hard transcript rule** — the critic notes it exceeds explicit source; make it a configurable floor (some examples imply 2:1).
5. **Quantify displacement** — "energetic/beefy" needs a concrete filter (e.g., body ≥ X×ATR or close beyond swing by ≥ N ticks); pick the threshold during WP1 and unit-test it.
6. **Add invalidation outputs** the synthesis under-modeled: **FVG two-touch invalidation** (3rd tap voids, Ep38) and **ITH/ITL breach** (Ep12); detectors must emit invalidation, not just formation.

#### 2.5.8 Long-tail concepts to add as detectors (from `overlookedConcepts`)
Power-of-Three (midnight & 08:30 anchored) · midnight-vs-08:30 open reference (use the **lower** when bearish) · SMT/inter-market divergence (ES↔NQ, EUR↔GBP, component currencies) · **Breaker block** (distinct from OB) · two-stacked-FVG stop rule · FVG two-touch invalidation · OB **mean-threshold 50%** as entry+partial · SD fib extensions · session macros (09:30 Judas, 12:00–13:00 lunch sweep, 13:30 PM algo, 15:00 spool, 15:40–16:00 MOC) · 3–4 PM consolidation reversal · overnight-run filter (skip AM if big 02:00–05:00 run → trade PM) · **HRLR** (high-resistance liquidity run — do NOT fade) · Sunday-gap S/R anchor · **sweep-vs-run** distinction · calendar gating (FOMC 2:00/2:30 knee-jerk, NFP Thu/Fri, NFP-week Wed+) · close-proximity entry (near, not inside, the FVG) · component-currency / risk-on-off FX bias.

#### 2.5.9 Optional broader sweep (long-tail, all 65 transcripts)
For exhaustive coverage beyond the entry model, the broad taxonomy sweep is still saved & resumable:
`Workflow({ scriptPath: "C:\Users\Mostafa\.claude\projects\C--Repos-Personal-ICT-transcribe\77104297-4990-41a9-b598-0603dfc9c8e4\workflows\scripts\ict-transcript-sweep-wf_b6717b76-098.js", resumeFromRunId: "wf_b6717b76-098" })`. The Mentorship entry-model mine is saved at `...\mentorship-entry-model-mine-wf_7f702dda-09a.js` (resume `wf_7f702dda-09a`).

#### 2.5.10 Web cross-check (validation, corrections, additions) — *the transcripts remain PRIMARY*
A web-research pass (`ict-2022-web-polish` workflow) validated §2.5 against community ICT material + forex microstructure, then an adversarial reviewer reconciled conflicts. **Rule: on any contested point, the transcript-mined model wins** (no primary ICT source was reachable online; all web sources are secondary educators). Confidence: MEDIUM.

- **Strongly corroborated (high confidence):** the full Sweep→MSS/Displacement→PD-array(FVG/OB)→OTE skeleton; 50% equilibrium premium/discount gate; sweep-must-precede-MSS; 3-candle FVG definition; the Daily→H1/H4→M15→M5/M3→M1 cascade; stop beyond swept extreme; DOL target list (IRL before ERL); London Open as the day's-high/low session; the **instrument-class killzone split** (FX NY 07:00–10:00 vs Index 08:30–11:00); tiered targets + trail-to-BE; 1% default risk; big-figure clustering.
- **Adopt as configurable additions (verifier-approved):** break-even-at-1R trigger (`BreakEvenAtR`, default 1.0, alongside the 75%→BE rule); aggregate **`MaxOpenPortfolioRiskPercent`** (≈5%) on top of per-trade risk; **FVG validity exclusions** (reject an FVG formed with no prior sweep / inside the Asian range / against HTF bias / with no CHoCH following / with overlapping c1–c3 wicks); **FVG displacement-quality filter** (`FvgMinGapPips` + `AtrMultiple`); **FVG mitigation state** (array dies once the gap is filled; LTF same-session, HTF days–weeks); **broken-swing-only** dealing-range fib anchoring + PD-array **inversion on BOS**; OB-in-FVG and fib-in-FVG overlap **bonus weights**; weekly-bias heuristic (≈80% of the weekly high/low forms Sun open→Tue London).
- **Keep PRIMARY default — REJECT the web override (contradictions):**
  1. **OTE fib anchor = BODY-to-body** (Mentorship) — the web's "anchor to wicks only" is rejected as default; wick-anchoring is the FOMC/NFP exception + a config toggle. *(Most important contradiction.)*
  2. **Min RR ≈ 2.5R configurable** (2:1 floor allowed) — do NOT raise to 3:1 on secondary authority (§2.5.7 caveat 4 says 2.5R already exceeds explicit source).
  3. **London Close 10:00–11:00** (entry-model §2.5.5 is more specific and wins over the §2.1 framework's 10:00–12:00 and the web's 10:00–12:00). Resolve the §2.1↔§2.5.5 inconsistency to 10:00–11:00.
  4. **No-trade lunch 12:00–13:00** (not the web's 14:00).
  5. **Targets = standard-deviation projections −1/−1.5/−2 SD** (not the web's negative-fib −0.27/−0.62 set — different construct, unverified).
  6. **Asian 19:00–00:00 NY** (not web 20:00); **FX NY 07:00–10:00** (the 08:00–11:00 cite is the index-class confusion).
- **Provenance flags (secondary/Primer-sourced → configurable, NOT Mentorship-verbatim):** 70.5% sweet spot, Silver Bullet (3 windows: London 03:00–04:00 / NY AM 10:00–11:00 / NY PM 14:00–15:00), the PD-array 7-tier ranking (authoritative weights stay §2.5.3), the 25–75% quadrant band, the "3-signal AND" bias formulation. **Never hard-code community win-rate/fill-rate stats** — configurable benchmarks only.
- **Open items to confirm against the transcripts later:** exact min-RR wording; whether negative-fib extensions are taught; per-instrument Asian-range trading flag; strict-first-FVG vs any-qualifying-FVG. Live swap rates drift → feed or operator-updated config.

---

## 3. Solution Structure & Scaffolding Commands

### 3.0 Domain-Driven Design (DDD) — the core discipline (logic lives in the domain)
**Hard rule:** *all* business logic lives in `IctTrader.Domain`. Application handlers, infrastructure,
and the API are thin shells that orchestrate, persist, and transport — they contain **no business
rules**. VSA is how we organize use-cases on the outside; **DDD is how the inside is modeled**. We treat
this as a single **bounded context** ("ICT trading analysis") with one **ubiquitous language** (sweep,
displacement, FVG, order block, OTE, killzone, bias, draw-on-liquidity, paper trade) used identically in
the transcripts (§2.5), the code, the alerts, and the tests.

**Building blocks (where each kind of logic goes):**
- **Value Objects** (immutable, self-validating, equality-by-value): `Price`, `Pips`, `PriceRange`,
  `RiskPercent`, `RewardRatio`, `OteZone`, `FibLevel`, `Symbol`/`SymbolSpec`, `Killzone`, `SessionWindow`,
  `Candle`, `Tick`. They reject invalid state in their constructors (e.g. negative pips, stop < 10 pips).
- **Aggregates & roots** (own invariants, expose intention-revealing methods, **private setters — no anemic
  models**):
  - `PaperTrade` (root) — `Open`, `RegisterFill`, `MoveStop`, `Close`; guards size/RR/lifecycle; freezes
    `InitialRiskPerUnit` so R is always vs original 1R.
  - `PaperAccount` (root) — equity, open-risk, loss-ladder state; authorizes new trades.
  - `Setup` (root) — the confirmed, advisory setup with `SetupReason`, targets, grade; `IsAdvisoryOnly`.
  - `ScanSession`/`SetupCandidate` — the per-symbol confluence FSM that accumulates conditions and
    confirms a `Setup` (a domain process, not a handler).
- **Domain Services** (pure, stateless, multi-object logic that isn't one aggregate's job):
  `SetupScorer` (confluence → grade), `IRiskManager` (effective risk %/sizing), `IFillEvaluator`
  (intrabar fill resolution), `PerformanceCalculator` (win rate/R/drawdown/equity), and every
  `ISetupDetector`. All deterministic, no I/O, no `DateTime.Now` (inject `IClock`).
- **Domain Events** (raised by aggregates, handled by application for side effects):
  `SetupConfirmed`, `PaperTradeOpened`, `PaperTradeFilled`, `PaperTradeClosed`,
  `DrawdownThresholdBreached`. Alerts/SignalR/persistence react to events — never inline in the domain.
- **Repositories = aggregate-scoped abstractions in the DOMAIN** (`Domain/Repositories/`):
  `ISetupRepository`, `IPaperTradeRepository`, `IPaperAccountRepository` — one per aggregate root,
  collection-like, **NO generic `IRepository<T>`**. Infrastructure implements them.
- **Ports (infra-facing) stay in Application** (`Application/Abstractions/`): `IMarketDataFeed`,
  `IAlertPublisher`, `ITradeExecutor` (only `SimulatedTradeExecutor`), `IClock`. These are I/O boundaries,
  not domain models.
- **Factories** for non-trivial construction (`PaperTradeFactory` from a confirmed `Setup` + account).

**Invariants enforced in the domain (examples):** a `PaperTrade` cannot open with stop < 10 pips or
qty ≤ 0; a counter-bias `Setup` can never confirm; `Close` is only legal from `Open`; risk % is clamped
by the loss-ladder before sizing. Handlers call these methods; they do not re-implement the checks.

**Reference direction (unchanged):** `Domain ← Application ← Infrastructure ← Api`. Domain depends on
nothing. This keeps detectors and aggregates trivially unit-testable and the methodology faithful.

### 3.0a Modular Monolith + in-memory message bus (NO MediatR)
**One deployable, many decoupled modules.** We ship a single host (operational simplicity) but structure
the code as a **modular monolith**: each module is an independent unit with its own public contract and
hidden internals, communicating **only** through an in-memory **message bus** — never by reaching into
another module's types. Because the only coupling is the bus + published contracts, any module can later
be lifted into its own microservice by swapping the bus transport (in-memory → Redis/RabbitMQ/Kafka) with
**zero change to module logic**.

**Why not MediatR:** MediatR is now commercially licensed. We use our own tiny `IMessageBus`
abstraction (≈3 methods) — no license, full control, and it doubles as the seam for future distribution.

**The bus (`SharedKernel/Messaging`):**
```csharp
public interface IMessageBus
{
    Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : ICommand;          // 1 handler
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default);                       // 1 handler
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent;                 // 0..N handlers
}
```
- Handlers: `ICommandHandler<T>`, `IQueryHandler<T,R>`, `IEventHandler<T>` — auto-registered by assembly scan.
- **Cross-cutting via decorators** (open/closed): logging, FluentValidation, metrics/timing, exception
  mapping, idempotency — wrapped around handlers (Scrutor-style decoration), not baked in.
- Default impl `InMemoryMessageBus` dispatches in-process; events fan out via `System.Threading.Channels`
  so publishing never blocks the hot scan path. Swapping to a distributed bus = one DI line.

**Modules (each = its own folder/project, `<Module>.Contracts` public, rest internal):**
| Module | Owns | Publishes | Subscribes |
|---|---|---|---|
| **MarketData** | feeds (OANDA/Finnhub/TraderMade/Replay/MT5), ingestion, candle/tick store | `CandleIngested`, `TickIngested` | — |
| **Scanning** | per-symbol `MarketContext`, the pure **Domain** detectors + confluence FSM | `SetupConfirmed`, `SetupRejected` | `CandleIngested` |
| **PaperTrading** | `PaperTrade`/`PaperAccount` aggregates, risk sizing, **realistic fill** (spread/slippage/commission/swap — §5.4) | `PaperTradeOpened/Filled/Closed` | `SetupConfirmed`, `CandleIngested`/`TickIngested` |
| **Performance** | win rate, R, drawdown, equity curve | `PerformanceUpdated` | `PaperTradeClosed` |
| **Alerting** | console + SignalR push, alert log | — | `SetupConfirmed`, `PaperTrade*`, `PerformanceUpdated` |
| **Host/Api** | composition root, endpoints, SignalR hub, options, `DefensiveModeGuard` | — | — |

Shared, dependency-free: **Domain** (the DDD model — §3.0) and **SharedKernel** (value objects, `IClock`,
`IMessageBus` + message marker interfaces, resource accessors). Module → Module references are **forbidden**;
a module references only `SharedKernel`, `Domain`, and other modules' `*.Contracts`. An ArchUnitNET test
enforces these boundaries (and the §6.3 guardrail).

### 3.1 Project layout (modular monolith; DDD core; group by MODULE then FEATURE)
```
IctTrader.sln
Directory.Build.props                 # net10.0, Deterministic, nullable enable, warnings-as-errors
docker-compose.yml                    # postgres (dev)
src/
  SharedKernel/                       # dependency-free: shared VOs, IClock, IMessageBus + ICommand/IQuery/IEvent markers, Resources accessors
  IctTrader.Domain/                   # PURE DDD model (shared by all modules): VOs + aggregates + domain services + events. No EF/bus/ASP.NET.
    ValueObjects/                     # Candle, Tick, Price, Pips, PriceRange, RiskPercent, RewardRatio, OteZone, FibLevel, Symbol/SymbolSpec, Direction, Timeframe
    MarketStructure/                  # SwingPoint, FairValueGap, OrderBlock, BreakerBlock, LiquidityPool, Displacement, MarketStructureShift (VOs)
    Sessions/                         # TradingSession, Killzone, SessionWindow, AsianRange, NyClock (DST-aware)
    Bias/                             # DailyBias, WeeklyBias, SmtDivergence
    Trading/                          # PaperTrade (agg root), PaperAccount (agg root), PaperTradeFactory, Fill, TradeStatus
    Setups/                           # Setup (agg root), ScanSession/SetupCandidate (confluence FSM), SetupReason, SetupGrade, PowerOfThree
    Styles/                           # TradeStyle (enum), TimeframePolicy (VO), TradeStyleClassifier (domain service) — §4.7
    Detection/                        # ISetupDetector, MarketContext, DetectorResult, Detectors/*  (pure domain services)
    Confluence/                       # ConfluenceRules, SetupScorer (domain service)
    Services/                         # IRiskManager, IFillEvaluator, PerformanceCalculator (domain services)
    Events/                           # SetupConfirmed, PaperTradeOpened/Filled/Closed, DrawdownThresholdBreached (domain events)
    Repositories/                     # ISetupRepository, IPaperTradeRepository, IPaperAccountRepository (aggregate-scoped; NO generic repo)
    Abstractions/                     # IClock
  Modules/                            # each module = Contracts (public) + Application (bus handlers) + Infrastructure (adapters/EF). Talk ONLY via the bus + *.Contracts.
    MarketData/
      Contracts/                      # events CandleIngested/TickIngested; commands StartFeed/StopFeed/ListFeeds; CandleDto
      Application/                    # ingestion handlers, IngestionPipeline, gap-backfill
      Infrastructure/                 # feed adapters Oanda/Finnhub/TraderMade/Replay/Mt5 + factory + resilient decorators; EF candle/tick store
    Scanning/                         # consumes CandleIngested; runs the pure Domain detectors + confluence FSM
      Contracts/                      # events SetupConfirmed/SetupRejected; queries GetActiveKillzone/ScanStatus; SetupDto
      Application/                    # CandleIngested handler -> MarketContext -> detectors -> SetupScorer -> publish SetupConfirmed
      Infrastructure/                 # SymbolScanner BackgroundService, ScannerRegistry, ISetupRepository (EF)
    PaperTrading/                     # consumes SetupConfirmed; opens/manages simulated trades with realistic costs (§5.4)
      Contracts/                      # events PaperTradeOpened/Filled/Closed; command ExecutePaperTrade; PaperTradeDto
      Application/                    # SetupConfirmed handler, FillSimulation BackgroundService
      Infrastructure/                 # SimulatedTradeExecutor (the ONLY ITradeExecutor), ExecutionCostModel, IPaperTrade/AccountRepository (EF)
    Performance/                      # consumes PaperTradeClosed; metrics + equity curve
      Contracts/ Application/ Infrastructure/   # PerformanceUpdated event; GetPerformanceSummary/EquityCurve queries; snapshot store
    Alerting/                         # consumes SetupConfirmed + trade/perf events
      Contracts/ Application/ Infrastructure/   # TradingHub (SignalR), console publisher, alert log (EF)
  IctTrader.Host/                     # composition root: minimal-API endpoint groups, SignalR map, DI + bus wiring, validated Options, DefensiveModeGuard
  Resources/                          # *.resx (AlertMessages, SetupReasons, ValidationMessages) — single home for ALL strings (no magic strings)
tests/
  IctTrader.UnitTests/                # xUnit: pure detector + domain tests (no container)
  IctTrader.IntegrationTests/         # xUnit + Testcontainers Postgres: per-slice
  IctTrader.E2E/                      # Reqnroll + Testcontainers + xUnit: Gherkin pipeline (MANDATORY)
web/
  ict-dashboard/                      # Vite + React + TS dashboard
```
**Reference rule:** a module references only `SharedKernel`, `Domain`, and other modules' `*.Contracts` — never another module's internals (ArchUnitNET-enforced). `Domain` depends on nothing → detectors/aggregates are trivially unit-testable. Bus handlers **orchestrate**; the domain **decides** (no business rule in a handler).

### 3.2 Scaffolding CLI commands
```bash
# --- Solution + shared + host (modular monolith; NO MediatR) ---
dotnet new sln -n IctTrader
dotnet new classlib -n IctTrader.SharedKernel -o src/SharedKernel       # IMessageBus + markers, shared VOs, IClock, Resources
dotnet new classlib -n IctTrader.Domain       -o src/IctTrader.Domain   # pure DDD model (depends on nothing)
dotnet new web      -n IctTrader.Host         -o src/IctTrader.Host      # minimal-API composition root

# --- One project trio per module: Contracts (public) + Application (bus handlers) + Infrastructure (adapters/EF) ---
for m in MarketData Scanning PaperTrading Performance Alerting; do
  dotnet new classlib -n IctTrader.$m.Contracts     -o src/Modules/$m/Contracts
  dotnet new classlib -n IctTrader.$m.Application    -o src/Modules/$m/Application
  dotnet new classlib -n IctTrader.$m.Infrastructure -o src/Modules/$m/Infrastructure
done

# --- Tests ---
dotnet new xunit -n IctTrader.UnitTests        -o tests/IctTrader.UnitTests
dotnet new xunit -n IctTrader.IntegrationTests -o tests/IctTrader.IntegrationTests
dotnet new xunit -n IctTrader.ArchitectureTests -o tests/IctTrader.ArchitectureTests   # module-boundary + guardrail tests
dotnet new install Reqnroll.Templates.DotNet
dotnet new reqnroll-xunit -n IctTrader.E2E     -o tests/IctTrader.E2E

dotnet sln add $(find src tests -name '*.csproj')

# --- References (enforce: module Application/Infrastructure -> SharedKernel + Domain + own Contracts + other modules' *.Contracts ONLY;
#     Domain & SharedKernel depend on nothing; Host -> every module's Application + Infrastructure). Script per-project, e.g.: ---
dotnet add src/Modules/Scanning/Application reference src/SharedKernel src/IctTrader.Domain src/Modules/Scanning/Contracts src/Modules/MarketData/Contracts
# ...repeat per module; module->module is ALWAYS *.Contracts, never Application/Infrastructure (ArchUnitNET-enforced).

# --- NuGet (NO MediatR — our own IMessageBus) ---
dotnet add src/SharedKernel package Scrutor                              # assembly scan + decorator registration for the bus
dotnet add src/Modules/Scanning/Application package FluentValidation     # (repeat for each module's Application)
dotnet add src/Modules/MarketData/Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/Modules/MarketData/Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL   # (repeat per module Infrastructure)
dotnet add src/IctTrader.Host package Microsoft.EntityFrameworkCore.Design
dotnet add src/IctTrader.Host package Swashbuckle.AspNetCore
dotnet add tests/IctTrader.UnitTests package FluentAssertions
dotnet add tests/IctTrader.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing Testcontainers.PostgreSql FluentAssertions Respawn
dotnet add tests/IctTrader.ArchitectureTests package TngTech.ArchUnitNET.xUnit FluentAssertions
dotnet add tests/IctTrader.E2E package Reqnroll.xUnit Microsoft.AspNetCore.Mvc.Testing Testcontainers.PostgreSql FluentAssertions Respawn

# --- Frontend ---
npm create vite@latest web/ict-dashboard -- --template react-ts
cd web/ict-dashboard && npm install
npm install @microsoft/signalr @tanstack/react-query lightweight-charts recharts date-fns date-fns-tz clsx   # lightweight-charts = OHLC + ICT overlays (§9.1); date-fns-tz = NY-time display (§4.8); recharts = equity curve only
npm install -D vitest @testing-library/react @testing-library/jest-dom jsdom openapi-typescript

# --- EF migration (after DbContext exists) ---
dotnet ef migrations add InitialCreate --project src/IctTrader.Infrastructure --startup-project src/IctTrader.Api
```

---

## 4. Scanning Engine (the heart of the system)

### 4.1 Data flow
```
[MarketData module] Feed (websocket/REST/CSV-replay, READ-ONLY)
  → IngestionHostedService → bus.PublishAsync(CandleIngested)            # MarketData's public event
[Scanning module] CandleIngestedHandler → IScannerRegistry.GetOrCreate(symbol) → ISymbolScanner.OnCandle(candle)
        (stateful, per-symbol, single-threaded via System.Threading.Channels)
        1. MarketContext.Append(candle)   # rolling window per timeframe; recompute session/killzone/Asian range
        2. run detector pipeline          # pure DOMAIN, deterministic
        3. feed matches → SetupCandidate  # confluence FSM accumulates conditions (domain)
        4. if candidate confirms + grades ≥ floor → bus.PublishAsync(SetupConfirmed)
[Scanning] SetupConfirmed built from Setup aggregate + SetupReason (from .resx templates) → ISetupRepository.Save
[PaperTrading] SetupConfirmedHandler → open PaperTrade (realistic costs §5.4) → bus.PublishAsync(PaperTradeOpened)
[Alerting] SetupConfirmedHandler + PaperTrade*Handler → SignalR + console (alert text from resources)
[Performance] PaperTradeClosedHandler → recompute metrics → bus.PublishAsync(PerformanceUpdated)
```
Modules never call each other directly — every arrow above is a bus message (`Publish` event / `Send` command).
**Key design choices**
- One long-lived **`SymbolScanner` per watched symbol** (hosted `BackgroundService`), warm-started from the DB (replay last N candles) → restart-safe.
- Each scanner owns one **`MarketContext`** (multi-timeframe ring buffers + live registries of open FVGs/OBs/liquidity pools/swings + session state + bias) and one **`SetupCandidate`** FSM.
- Per-symbol bounded **Channel** → in-order, single-threaded processing → **determinism** (same candles → same setups; replay reproduces live bit-for-bit).
- **Multi-timeframe top-down:** HTF bias on H1/H4/D1 gates LTF entries on M1/M5. A counter-bias LTF signal does not advance the candidate.
- Only the **graded confluence** publishes the heavier `SetupConfirmed` event — the hot path stays lean.

### 4.2 Detector abstraction (pure)
```csharp
public interface ISetupDetector
{
    ConfluenceCondition Condition { get; }
    DetectorResult Detect(MarketContext ctx, Candle current); // pure: no I/O, no DateTime.Now
}
public readonly record struct DetectorResult(
    bool Matched, Direction? Direction, decimal? KeyLevel,
    string ReasonFragment, IReadOnlyDictionary<string, object>? Evidence);
```
Detectors to implement (each cites the ICT rule it encodes). **Core set** (build first, WP1): `SwingPointDetector`, `FairValueGapDetector` (incl. top-down first-FVG, stacked-FVG flag, two-touch invalidation), `OrderBlockDetector` (requires FVG; opening-price + 50% mean-threshold), `LiquidityDetector`, `LiquiditySweepDetector`, `DisplacementDetector` (quantified energy filter — §2.5.7), `MarketStructureShiftDetector`, `DealingRangeEquilibriumDetector` + `PremiumDiscountGateDetector`, `OteFibDetector`, `DrawOnLiquidityDetector`, `KillzoneClock` (instrument-class aware), `DailyBiasDetector`, `AsianRangeDetector`, `JudasSwingDetector`/`PowerThreeDetector`, `StopPlacementCalculator`, `StopManagementEngine`, `TargetLadderBuilder`, `RiskSizingEngine`, `NeutralConditionDetector`. **Extended set** (§2.5.6/§2.5.8): `BreakerBlockDetector`, `SmtDivergenceDetector`, `StandardDeviationProjectionDetector`, `EconomicCalendarFilter`, `SessionMacroDetector`, plus the long-tail (HRLR suppressor, sweep-vs-run, Sunday-gap anchor). **The full, citation-backed detector set is §2.5.6.**

### 4.3 Example detector — FVG (ICT: 3-candle imbalance)
```csharp
public DetectorResult Detect(MarketContext ctx, Candle current)
{
    var w = ctx.Window(current.Timeframe);
    if (w.Count < 3) return DetectorResult.NoMatch;
    var (c1, c3) = (w[^3], w[^1]);
    if (c1.High < c3.Low)   // bullish gap
    { ctx.OpenFvgs.Add(new FairValueGap(Direction.Bullish, c1.High, c3.Low, w[^2].OpenTimeUtc));
      return Match(Direction.Bullish, c3.Low, $"Bullish FVG ({c1.High}-{c3.Low})"); }
    if (c1.Low > c3.High)   // bearish gap
    { ctx.OpenFvgs.Add(new FairValueGap(Direction.Bearish, c3.High, c1.Low, w[^2].OpenTimeUtc));
      return Match(Direction.Bearish, c3.High, $"Bearish FVG ({c3.High}-{c1.Low})"); }
    return DetectorResult.NoMatch;
}
```

### 4.4 Confluence state machine + grading
- **`SetupCandidate`** (one per symbol) accumulates matched `ConfluenceCondition`s with a **direction lock** (first directional match sets bias; contradictory matches ignored) and **per-condition TTL** (decays if the confluence stalls).
- Confirms when `RequiredConditions` (config, e.g. `InKillzone + LiquiditySweep + MarketStructureShift`) are all present.
- **`SetupScorer`** assigns config-driven weights per condition → score 0–100 → grade A/B/C/Reject. Only grade ≥ `AlertMinimumGrade` (e.g. B) fires an alert; lower grades are stored silently for review. **This gate is what makes "high-probability" concrete.**

### 4.5 `ScanMarketDataFeature` (required core feature — in the Scanning module)
- The Scanning module consumes `CandleIngested`, runs the pure domain, and when the `SetupCandidate` confirms + grades ≥ floor it composes **`SetupReason`** (structured clauses ordered bias→killzone→sweep→MSS→FVG/OB→OTE, rendered from **`.resx` templates** into a human sentence), builds the advisory `Setup` aggregate (entry/stop/targets/RR), persists via `ISetupRepository` (JSONB reason+evidence), and **publishes `SetupConfirmed`** on the bus. The **Alerting** module's `SetupConfirmedHandler` does the SignalR push + console log (alert text from resources) — Scanning never touches transport.
- Example emitted reason (from `SetupReasons.resx`): *"Bullish FVG (1.0832–1.0840) formed inside London Open Killzone after Asian High sweep, MSS confirmed at 1.0828, OTE 0.705 entry 1.0835, R:R 2.4."*

### 4.6 Configuration (Options pattern — no magic numbers) + Resources (no magic strings)
**No magic numbers:** all ICT/trading constants in `appsettings` under `Ict:*`: `Killzones` (NY windows + tz, instrument-class), `Symbols` (pip size/digits), `Judas` (sweep pips/window), `Fibonacci` (entry/sweet-spot/targets), `Risk` (min RR, risk %, loss-ladder), `Confluence` (required conditions, weights, grade thresholds, alert floor), `Scanning` (watched symbols + `ActiveStyles`, window capacities, warmup bars), `TradeStyles` (per-style Bias/Structure/Entry timeframes + hold + RR — §4.7), `Time` (`ReferenceTimeZone` default `America/New_York`, `DisplayTimeZone` — §4.8), and `Execution` (spread/commission/slippage/swap — §5.4). Bound to validated POCOs with `ValidateOnStart()`.

**No magic strings:** every human-facing/log/alert/validation/reason string lives in **`.resx` resource files** (`Resources/AlertMessages.resx`, `SetupReasons.resx`, `ValidationMessages.resx`), accessed via a generated strongly-typed accessor — never inline literals. The `SetupReason` (§4.5) is built from parameterized resource templates so it is localization-ready and consistent. Enum/string keys (killzone names, condition names) are defined once as enums/`const`, mapped to display text in resources.

**Selectable killzone (requirement):** `Ict:Scanning:ActiveKillzones` is a configurable list controlling **which killzones the scanner actively hunts setups in**. The `InKillzone` confluence condition only matches when the current candle falls inside an *enabled* killzone, so the operator can choose to trade only London Open, only New York, etc.
```jsonc
"Ict": { "Scanning": { "ActiveKillzones": ["LondonOpen", "NewYorkOpen"] } }   // default = ICT's primary preference
```
ICT's stated preference (from the transcripts): **London Open** "generally has the highest probability of creating the high or the low of the day," and the **New York AM** session is prime for USD pairs (and is the home of the Silver Bullet 10:00–11:00 NY window). Default `ActiveKillzones` to `["LondonOpen","NewYorkOpen"]`; allow any subset of `Asian | LondonOpen | NewYorkOpen | LondonClose`. The frontend exposes a killzone toggle that PATCHes this setting.

### 4.7 Trade style & timeframe selection (scalp / day-trade / swing / position) — timeframe "from the resources"
ICT teaches **top-down timeframe alignment**: an HTF sets bias/draw-on-liquidity, an intermediate TF frames structure + PD arrays, an LTF refines the entry. The transcript-mined cascade (§2.5.10, high confidence): **Daily(bias) → H1/H4 → M15(pools) → M5/M3(MSS+FVG) → M1(refinement)**. A **`TradeStyle`** selects which slice of that cascade the scanner uses, so each setup is hunted and labelled at the *correct* timeframes — and the operator chooses which style(s) to run.

**Domain (pure):** `TradeStyle` (enum `Scalp | Intraday | Swing | Position`) + `TimeframePolicy` value object (`BiasTf`, `StructureTf`, `EntryTf`, expected-hold, valid killzones, RR profile) + `TradeStyleClassifier` domain service that (a) returns the TF set for a chosen style and (b) **classifies a detected setup's style** from the TF it formed on + expected hold/target distance. The `Setup` aggregate carries its `TradeStyle` and the timeframe it triggered on.

**Config-driven, ICT-grounded defaults** (`Ict:TradeStyles` — no magic numbers; the table IS "the resources", sourced from the ICT cascade, fully tunable; confirm exact pairings against the transcripts like the §2.5.7 caveats):
| Style | Bias TF | Structure TF | Entry TF | Typical hold | Notes |
|---|---|---|---|---|---|
| Scalp | H1 / M15 | M5 / M3 | M1 (sub-min) | minutes–~30m | Silver-Bullet direct-FVG entries allowed (skip OTE retrace) |
| **Intraday / day-trade** *(default — the §2.5 model)* | Daily / H4 | M15 / M5 | M1–M5 | ≤ 90–120 min, no overnight | full §2.5 sweep→MSS→FVG/OB→OTE |
| Swing | Weekly / Daily | H4 / H1 | M15 | days | weekly DOL; wider stops/targets |
| Position | Monthly / Weekly | Daily | H4 | weeks | HTF PD arrays only |

The scanner runs the TF set(s) for the **active styles** (`Ict:Scanning:ActiveStyles`, default `["Intraday"]`); each `MarketContext` keeps exactly the TF ring buffers those styles need, and the §2.5 entry model is evaluated per style with its TF triple (RR floor + hold cap scale by style). Every alert/Setup gets a **style badge** + trigger timeframe; the dashboard filters by style.

> *(The 2022-Mentorship mining that produced §2.5 is complete — see §2.5/§2.5.10. Re-run the saved workflows only to refresh rules or confirm the per-style timeframe pairings above.)*

### 4.8 Time-zone awareness (ICT times are New York; the host may be anywhere)
ICT killzones/sessions are defined in **New York wall-clock time with US DST**. The system must produce **identical** results regardless of the server/container/browser timezone. Non-negotiable rules:
- **UTC is the only source of truth.** All candle/tick/setup/trade timestamps are stored and moved as UTC (`timestamptz`); the domain gets UTC via **`IClock`** — NEVER `DateTime.Now` / `DateTimeOffset.Now` / `TimeZoneInfo.Local` / the ambient process zone anywhere in detectors, handlers, or hosts.
- **NY conversion lives only in `NyClock`**, a DST-aware service using the **IANA id `America/New_York`** via `TimeZoneInfo.FindSystemTimeZoneById("America/New_York")` (cross-platform on .NET 10 through **ICU**). NEVER the Windows id `"Eastern Standard Time"` (wrong off-Windows and ignores IANA rules). The id is config (`Ict:Time:ReferenceTimeZone`, default `America/New_York`) so it's not a magic string, but validation locks it to NY.
- **ICU guaranteed on every host:** `Directory.Build.props` sets `<InvariantGlobalization>false</InvariantGlobalization>`; the Docker image bundles ICU (e.g. `icu-libs` on Alpine). A startup validator **fails fast** if `America/New_York` cannot be resolved, so a mis-provisioned host never silently mis-times killzones.
- **Killzone math** (`KillzoneClock`) converts each candle's UTC open into NY local, then tests the NY window — so "London Open 02:00–05:00 NY" is correct in summer *and* winter and identical whether the host runs in UTC, Tokyo, or Berlin. DST-transition days (spring-forward/fall-back) and session boundaries are unit-tested with the process zone forced to non-NY (`Asia/Tokyo`, `Etc/UTC`).
- **Frontend** renders times in **NY by default** (labelled "NY") with an optional toggle to the viewer's local zone, formatting from the UTC value via `date-fns-tz` + `America/New_York` — the browser's own zone changes only *display*, never classification.

---

## 5. Paper-Trading Engine & Performance (the second required core feature)

### 5.1 `ExecutePaperTradeFeature`
- `ExecutePaperTradeCommand` (built from a confirmed `Setup`) → handler computes **effective risk %** via `IRiskManager` (loss-tiering + win-cycle), derives **position size** from risk%, equity and stop distance, creates a `PaperTrade` aggregate (entry/stop/targets/size/RR/killzone/reasoning), opens it through **`SimulatedTradeExecutor`** only, persists.
- **Position sizing:** `riskPerUnit = |Entry-Stop|`; `stopPips = riskPerUnit / pipSize`; `riskAmount = equity * effRisk%/100`; `qty = floor(riskAmount / (stopPips * valuePerPip) / lotStep) * lotStep`. Guards: stop ≥ 10 pips; qty > 0 else domain error.
- **`IRiskManager`** (pure): after a loss → 1%, 2nd loss → 0.5%, restore after 50% recovery of the tier dip; **5 consecutive wins → drop to lowest unit**; clamp to 3% / configured max; win-cycle override evaluated first.

### 5.2 Trade lifecycle / fill simulation
- **`FillSimulationService`** (hosted) pumps each incoming candle/tick into the pure **`IFillEvaluator`**: `Pending → Open` (entry touched) → partial scale-outs at fib targets → breakeven arm → `Closed` (stop/final target/time-exit).
- **Intrabar ambiguity:** when one candle straddles both stop and target, resolve by `FillAssumptions.StopVsTarget` (default **WorstCase = StopFirst**, conservative) using the **Open→Low→High→Close** touch sequence (long stop fills when `bar.Low ≤ stop`, long TP when `bar.High ≥ target` — never close-only, so ICT wick-sweeps can't falsely survive). All costs come from the `IExecutionCostModel` (**§5.4**). Tick / sub-bar replay removes the ambiguity → gold-standard test path.
- **R booking:** `InitialRiskPerUnit` frozen at open → R always measured vs original 1R even after breakeven.

### 5.3 Performance tracking (`Features/Performance`)
Pure **`PerformanceCalculator`** over closed trades:
```
WinRate = wins/total ; AvgR = ΣR/total ; ProfitFactor = ΣwinPnl / |ΣlossPnl|
Expectancy = WinRate*AvgWin − LossRate*AvgLoss
EquityCurve: E_k = E_{k-1}+pnl_k ; Peak_k=max ; DD_k=(Peak_k−E_k)/Peak_k ; MaxDD=max DD_k
```
Plus per-killzone and per-setup-type breakdowns, time-in-trade distribution. Hybrid compute-on-read (authoritative) + debounced `PerformanceSnapshot` cache for dashboard latency.

### 5.4 Trade realism & execution cost model (senior-trader honesty) — `Ict:Execution:*`
A pure domain service **`IExecutionCostModel`** applies broker-realistic costs to every simulated fill so paper-trade P&L reflects what a real account would experience — the only way the performance numbers can "prove the strategy" honestly. **Everything is configurable** (no magic numbers); the values below are *starting defaults* an operator tunes per broker/instrument, sourced from the §2.5.10 web cross-check (secondary sources → defaults only, never hard-coded expectations).
```jsonc
"Ict": { "Execution": {
  "Spread":      { "Model": "SessionStep", "BasePips": 0.7, "PeakPips": 0.7, "AsianPips": 1.4, "NewsPips": 6.0 },
  "Commission":  { "PerLotRoundTripUsd": 6.0 },                 // ECN; ≈0.6 pip EURUSD, separate cost line
  "Slippage":    { "EntryPips": 1.0, "StopPips": 2.0, "Tier": "Normal", "OffPeakPips": 3.0, "StressPips": 8.0 },
  "Swap":        { "LongPerLotPerNightUsd": -7.0, "ShortPerLotPerNightUsd": 2.3, "CutoffEt": "17:00", "TripleDay": "Wednesday", "TripleMultiplier": 3 },
  "Weekend":     { "FxCloseNy": "Fri 17:00", "FxReopenNy": "Sun 17:00", "GapFillAtReopenOpen": true },
  "Fills":       { "IntrabarSequence": "OpenLowHighClose", "PartialFillAdvThresholdPercent": 5, "MinStopDistancePips": 1, "LatencyMs": 40 },
  "Instruments": { "EURUSD": { "PipValueUsdPerLot": 10, "ContractSize": 100000 } }  // ES/NQ per contract spec
} }
```
- **Spread** — session step-function; entries cross the spread (buy@ask/sell@bid); widen off-peak (Asian) and at scheduled news (NFP/FOMC/CPI minute, keyed to an economic-calendar service).
- **Commission** — round-turn USD/lot, converted to pips for signal-quality thresholds.
- **Slippage** — separate entry vs stop, Normal/OffPeak/Stress tiers; **stops slip more, never positive** slippage; ~2 pips per 500 ms latency in fast markets.
- **Swap/rollover** — long/short per lot/night (ideally from a live rate-differential feed — values drift with Fed/ECB policy — else operator-set); financing charged at the **17:00 ET** cutoff; **triple on Wednesday** for weekend settlement.
- **Weekend gap** — Friday stops cannot fill intrabar over the weekend; they fill at the **Sunday-reopen open** (gap-through), so weekend-risk tails (CHF-2015-style) are modeled.
- **Partial fills / min stop / latency** — partials only above an ADV threshold (retail majors fill fully); broker min-stop/freeze level as a floor under the structure-based sweep-wick stop; latency delays the fill.
- **Sweep fill rule** — a wick beyond a level that closes back inside still fills resting stops at `level + spread` (bullish sweep: `bar.High > swingHigh AND bar.Close < swingHigh` → buy-stops fill ~`swingHigh + spread`).
- **Pip value / contract size** — per instrument, for cost↔pip↔USD conversion and risk-based position sizing. The `PaperTrade` aggregate books P&L net of spread + commission + slippage + swap.

---

## 6. Data-Feed Integration & MT5 Bridge

### 6.1 `IMarketDataFeed` abstraction + factory (enum-driven strategy-factory)
`IsReadOnly` always true (enforced by `ReadOnlyFeedGuard` decorator). Adapters selected by `MarketFeedProvider` enum + options; new provider = new impl + 1 DI line. All wrapped in `ResilientMarketDataFeed` (reconnect/backoff ≤ ~15s, REST gap-backfill).

| Provider | Auth | Historical REST | Streaming | Free-tier notes |
|---|---|---|---|---|
| **OANDA practice** *(recommended default)* | Bearer (practice token) | v20 `/instruments/{i}/candles` (M1..W1) | v20 `/pricing/stream` (true bid/ask) | Practice account = **structurally non-live**; broker-accurate; generous limits |
| **Finnhub** *(secondary/fallback)* | `token` query/header | `/forex/candle` | `wss://ws.finnhub.io` | ~60 calls/min; broad assets; synth FX spread |
| **TraderMade** *(tertiary FX)* | `api_key` query | `/timeseries`,`/hour_historical` | WebSocket FX quotes | Tight free monthly cap |
| **Replay** *(tests/backtest)* | — | from DB/CSV | yields in chrono order | Drives `FakeClock` → bit-reproducible |

Symbol mapping normalized to domain `Candle`/`Tick`; all timestamps stored UTC (`timestamptz`), NY conversion only in `NyClock`.

### 6.2 MT5 bridge (interface only — read-only market data)
`IMt5Bridge` exposes `ConnectAsync`, `SubscribeSymbolAsync`, and events `OnTick`/`OnBar`/`OnConnectionChanged` — **no order-routing methods exist** (cannot call what isn't there). JSON-lines over TCP (MTsocketAPI/MtApi-style EA); contract has only `subscribe` + inbound `tick`/`bar`/`hello`; bridge **refuses non-DEMO** accounts. Plugs into the same feed factory as `MarketFeedProvider.Mt5`; a `Mt5ListenerService` owns the socket lifecycle.

### 6.3 Defensive guarantees (structural — §0 of design)
1. Only `SimulatedTradeExecutor` implements `ITradeExecutor`; no broker SDK anywhere.
2. `LiveTradingEnabled` defaults false and an `IValidateOptions<>` **fails startup if ever true** (the flag exists only to be asserted false).
3. Feeds read-only by type; sandbox-only credentials (`Environment != "live"`).
4. `DefensiveModeGuard` hosted service asserts no broker assembly is loaded at boot.
5. CI architecture test: no `ITradeExecutor` impl other than Simulated; no live-trading API references. Every `Setup` carries `IsAdvisoryOnly = true`; SignalR contract has no "execute" message.

---

## 7. EF Core Persistence

Entities: `CandleEntity`, `TickEntity`, `SetupEntity`, `SetupCandidateSnapshotEntity`, `TradingSessionEntity`, `PaperTradeEntity`, `PaperAccountEntity`, `PerformanceSnapshotEntity`, `RejectionLogEntity`.
- **JSONB** (`.ToJson()` / `jsonb`): `Setup.Reason` (structured clauses), `Setup.Targets`, `Setup.Evidence`; `Tick.Raw`/`Candle.Extra` (provider payload); `PaperTrade.Targets`/`Fills`.
- **Decimals:** prices `numeric(18,8)`, currency `(18,2)`. Enums as strings. Concurrency token `xmin`.
- **Indexes:** candles unique `(Symbol, Timeframe, OpenTimeUtc)` (idempotent ingestion + fast warmup); setups `(Symbol, DetectedAtUtc DESC)`, `(Grade)`; paper trades `(AccountId, Status)`, `(Symbol, Status)`, `(AccountId, ClosedAt)`, `(AccountId, Killzone)`; ticks `(Symbol, TimeUtc)` + GIN(payload). Consider TimescaleDB hypertable / monthly partition for candle retention.
- Code-first migrations from `Infrastructure` with `Api` as startup; design-time `IDesignTimeDbContextFactory`. Seed migration: `SymbolSpec` rows + default DEMO `PaperAccount`.

---

## 8. Testing Strategy (E2E mandatory)

### 8.1 Pyramid
| Layer | Project | Infra | Proves |
|---|---|---|---|
| Unit | `IctTrader.UnitTests` | none | each detector pure/deterministic; risk/fill/perf math; killzone boundaries; DST |
| Integration | `IctTrader.IntegrationTests` | Testcontainers Postgres (class fixture) | one slice persists/reads correctly |
| **E2E (mandatory)** | `IctTrader.E2E` | Testcontainers Postgres + ReplayFeed | full pipeline: candles → Setup → alert → paper trade → fill → performance |

### 8.2 Mechanics
- **Reqnroll** generates xUnit classes from `.feature`; **Testcontainers** boots Postgres once per run (`[BeforeTestRun]`, apply migrations); **Respawn** truncates between scenarios (`[BeforeScenario]`).
- **`CustomWebApplicationFactory<Program>`** swaps three seams via `ConfigureTestServices`: EF → container connection string, `IMarketDataFeed` → `ReplayMarketDataFeed`, `IClock` → `FakeClock`, `ISetupAlertNotifier` → `CapturingNotifier`; disables the background hosted loop (tests pump the pipeline). `Program.cs` exposes `public partial class Program {}`.
- **Deterministic fixtures:** `BullishLondonSetupFixture` — a hand-built M1 candle series encoding Asian range → Judas sweep of Asian low → bullish MSS displacement → **bullish FVG inside London killzone** → OTE retrace entry → run to target (named anchors, no magic numbers). `RejectedOutsideKillzoneFixture` — identical but FVG forms at ~06:30 NY (dead time) → rejected.

### 8.3 Gherkin `.feature` (excerpt — full file in WP9)
```gherkin
Feature: ICT setup detection and paper-trade execution
  Background:
    Given a clean trading database
    And the symbol "EURUSD" is being analysed
    And the market clock is anchored to New York time

  Scenario: A valid bullish London setup is detected, paper-traded, and hits target
    Given the replay feed is loaded with the "BullishLondonKillzone" candle fixture
    When the replay feed is played to completion
    Then a Setup should be detected for "EURUSD" with direction "Long"
    And the setup should reference the killzone "London"
    And the setup reasoning should contain "Bullish FVG"
    And a paper trade should be opened from that setup
    And the paper trade should close with status "TargetHit"
    And exactly 1 "setup detected" alert should have been pushed over SignalR
    And the performance summary should show a win rate of 100 percent over 1 trade

  Scenario: A setup is rejected because the FVG forms outside any killzone
    Given the replay feed is loaded with the "FvgOutsideKillzone" candle fixture
    When the replay feed is played to completion
    Then no Setup should be detected for "EURUSD"
    And no paper trade should be opened

  Scenario Outline: Killzone classification at session time boundaries
    Given a candle opening at "<ny_time>" New York time
    When the killzone for that candle is evaluated
    Then the killzone should be classified as "<killzone>"
    Examples:
      | ny_time | killzone |
      | 01:59   | Asian    |
      | 02:00   | London   |
      | 05:00   | London   |
      | 06:59   | None     |
      | 07:00   | NewYork  |
```
Step definitions use Testcontainers (`PostgresContainerFixture : IAsyncLifetime`), Reqnroll `IObjectContainer` DI, the `CustomWebApplicationFactory`, and `FluentAssertions`; full driver in WP9. Use ICU id `America/New_York` (never Windows `"Eastern Standard Time"`).

---

## 9. React Frontend Dashboard

- **Layout** (CSS grid, collapses < 1024px): **center = the ICT Pattern Chart** (candlestick + live overlays, the centerpiece — §9.1); left **Alerts Feed** (reasoning string + killzone badge + direction + **style** chip); right/bottom **Active Paper Trades** table (entry/stop/target/status/time-in-trade/live R) + **Performance** (win rate, avg R:R, max DD, equity curve). Clicking an alert/trade focuses the chart on that setup; a **style filter** (Scalp/Intraday/Swing/Position) and symbol/timeframe switcher sit in the chart header.
- **Data:** React Query owns server state (`['candles',sym,tf]`, `['overlays',sym,tf]`, `['trades','active']`, `['alerts']`, `['performance']`); **SignalR** pushes deltas merged via `setQueryData`; ~30s reconcile. Equity curve via Recharts; price chart via lightweight-charts (§9.1).
- **Types:** `web/ict-dashboard/src/types/api.ts` mirrors backend DTOs byte-for-byte; generated from OpenAPI (`openapi-typescript`) in CI so they can't drift.
- **SignalR:** `createTradingHub(baseUrl)` connects `/hubs/trading`, handlers `SetupDetected`/`TradeUpdated`/`PerformanceUpdated` + `CandleAppended`, `withAutomaticReconnect`. `useTradingHub` wires into React Query; new setups stream onto the chart live.
- **Visual intent:** dark trading-desk theme, monospaced tabular numerals for price/R alignment, semantic color (green long/win, red short/loss, amber pending), killzone badge colors (Asian indigo, London teal, NY orange). Apply the **frontend-design** skill for the final typography/palette/spacing pass.

### 9.1 ICT Pattern Chart (candlestick + concept overlays — "I like them visually")
**Library: TradingView `lightweight-charts` (v5, free/OSS)** — candlestick series + markers + price lines + rectangle **primitives** for zones; far better for OHLC + annotations than Recharts (which stays for the equity curve only). A thin `IctChart` React wrapper renders candles for the selected symbol/timeframe and draws every detected ICT concept as an overlay, so the operator *sees* the setup the engine found. The geometry comes straight from the `Setup`'s `Evidence` JSONB (zone bounds, levels, timestamps) → the chart shows exactly what the detectors saw. Endpoint `GET /api/chart/{symbol}?tf=&style=` returns candles + active overlays; SignalR appends new candles + setups live.

| ICT concept (detector) | Visual overlay |
|---|---|
| **Fair Value Gap** | translucent rectangle across the gap (green bullish / red bearish); fades/greys when **mitigated** (two-touch) |
| **Order block / Breaker** | bordered rectangle at OB open→range with `OB`/`BRK` label + 50% mean-threshold line |
| **Liquidity pool / equal highs-lows** | dashed horizontal line + "liquidity" tag (buy-side above / sell-side below) |
| **Liquidity sweep (Judas)** | triangle marker on the wick that raided the level |
| **MSS / displacement** | arrow marker + line at the broken swing; displacement candle highlighted |
| **OTE zone** | shaded 62–79% band with the 70.5% sweet-spot line |
| **Killzone** | vertical background band (Asian indigo / London teal / NY orange / PM amber) |
| **Entry / Stop / Targets** | price lines (entry blue, stop red, T1/T2 green) with R labels |
| **Daily bias / draw-on-liquidity** | HTF level line + arrow pointing at the targeted liquidity |
| **Trade style + timeframe** | header badge (Scalp/Intraday/Swing/Position) + the trigger TF |

Overlays toggle individually (a legend), animate in on `SetupDetected`, and a replay-scrubber lets the operator step a fixture candle-by-candle to watch a pattern form (great for the E2E demo). Accessibility: every overlay also appears as text in the alert reasoning, so the chart is an enhancement, not the only channel.

---

## 10. (reserved)

---

## 11. Multi-Agent Execution Plan

### 11.1 Frozen contracts (Work Package 0 lands & tags `contracts-v1` BEFORE anything else)
1. `Direction`, `Killzone` enum member names (Gherkin + TS depend on exact strings).
2. `Candle`/`Tick` shape + UTC convention.
3. Abstractions: `IClock`, `IMarketDataFeed`, `ISetupStore`, `IAlertPublisher`/`ISetupAlertNotifier`, `IIngestionPipeline`, `IRiskManager`, `ITradeExecutor`, `IFillEvaluator`.
4. DTO field names (camelCase JSON) mirrored in `types/api.ts`.
5. Killzone NY windows (locked by the Scenario Outline).
6. SignalR route `/hubs/trading` + method names (incl. `CandleAppended`); REST routes `GET /api/trades/active`, `/api/alerts`, `/api/performance`, `/api/chart/{symbol}?tf=&style=` (candles + ICT overlays, §9.1), `POST /api/paper-trades`.
7. `TradeStyle` enum names (Scalp|Intraday|Swing|Position) + `TimeframePolicy` shape (§4.7) — the dashboard style filter + chart badge depend on these strings.

Any change to 1–6 after freeze = coordinated version bump + notice to WP7/8/9.

### 11.2 Work packages
| # | Work Package | Owns | Depends on | Definition of Done |
|---|---|---|---|---|
| 0 | **Contracts & skeleton** | sln, project refs, Domain primitives, Application abstractions + DTOs, `Program.cs` (`public partial class Program`) | — | builds empty; contracts tagged `contracts-v1` |
| 1 | **Domain detectors + trade-style** | `Domain/Detection/*` all detectors + `MarketContext` + `NyClock` + `Domain/Styles/*` (`TradeStyle`/`TimeframePolicy`/`TradeStyleClassifier`, §4.7) | 0 (encode §2.5 entry model + §2.5.6 detector set; heed §2.5.7 caveats) | every detector pure; style→timeframe policy unit-tested; boundaries/DST/invalidation pass |
| 2 | **Persistence** | `Infrastructure/Persistence/*` entities, DbContext, migrations | 0 | `migrations add` succeeds; round-trip integration test |
| 3 | **Scan slice** | `Features/Scan/*` + `SetupComposer` + confluence wiring | 0,1,2 | crafted candles → expected Setup/rejection; reasoning correct |
| 4 | **Paper-trade slice** | `Features/PaperTrades/Execute*` + aggregates + `IRiskManager` | 0,2 | Setup → one PaperTrade with correct size/RR |
| 5 | **Fill evaluator** | `Features/PaperTrades/EvaluateFills` + `FillSimulationService` | 0,2,4 | open trade closes at stop/target; R correct |
| 6 | **Performance slice** | `Features/Performance/*` + `PerformanceCalculator` | 0,2,5 | closed trades → correct metrics + equity curve |
| 7 | **Feeds + host + SignalR** | feed adapters + factory/decorators, `IngestionPipeline`, scanner host, `TradingHub`, endpoints, DI, `DefensiveModeGuard` | 0,3,4,5,6 | app runs; candle → SignalR push; REST returns DTOs; OpenAPI emitted |
| 8 | **Frontend** | `web/ict-dashboard/**` incl. the **ICT Pattern Chart** (§9.1, lightweight-charts) | 0 (DTOs only) | chart renders candles + all concept overlays (FVG/OB/sweep/MSS/OTE/killzone/entry-stop-TP) and live-updates via SignalR; 3 side panels render; **style filter** works; typecheck + vitest green |
| 9 | **E2E Gherkin** | `tests/IctTrader.E2E/**` features, steps, hooks, fixtures, factory, doubles | 0,3,4,5,6,7 | both scenarios + Outline pass on Testcontainers + ReplayFeed |

> **Definition-of-Done applies to EVERY WP (in addition to the row):** (a) Clean Code — no magic numbers (Options) / no magic strings (`.resx`); (b) module boundaries hold (ArchUnitNET test green; no MediatR; no cross-module internals); (c) **ICT conformance** — any trading-logic change reviewed against §2.5/§2.5.10 via `/ict-conformance` or the `ict-domain-expert` agent; (d) defensive guardrail re-checked (§6.3); (e) tests green at the right level.

### 11.3 Parallelism
- **Phase A (blocking):** WP0 → freeze contracts.
- **Phase B (parallel):** WP1 (detectors), WP2 (persistence), WP8 (frontend on mocks).
- **Phase C:** WP3 after 1+2; trading chain **WP4 → WP5 → WP6** sequential while WP3 runs in parallel.
- **Phase D:** WP7 composes once 3,4,5,6 land.
- **Phase E (gate):** WP9 E2E once 7 up; WP8 connects to live API in parallel.
- **Critical path:** `0 → 2 → 4 → 5 → 6 → 7 → 9`. Detector + frontend tracks have slack — start them first.

---

## 12. Verification (how to prove it works end-to-end)
1. **Unit:** `dotnet test tests/IctTrader.UnitTests` — every detector against hand-built candle fixtures (FVG, swing, sweep, OTE R:R rejection, killzone boundaries, DST spring-forward/fall-back); risk-tiering table; fill intrabar matrix; performance formulas. **Time-zone independence:** the killzone Scenario Outline runs with the process timezone forced to `Asia/Tokyo` and `Etc/UTC` and must yield identical classifications (§4.8).
2. **Integration:** `dotnet test tests/IctTrader.IntegrationTests` — each slice against ephemeral Testcontainers Postgres; JSONB round-trips; index usage.
3. **E2E (mandatory):** `dotnet test tests/IctTrader.E2E` — Gherkin scenarios drive candles → Setup → alert (CapturingNotifier) → paper trade → fill at target → performance = 100% win over 1 trade; plus the rejection scenario and killzone Scenario Outline.
4. **Manual run:** `docker compose up postgres`; `dotnet run --project src/IctTrader.Api`; point feed at **OANDA practice**; `npm run dev` in `web/ict-dashboard`; confirm alerts appear with reasoning strings, paper trades open/close, performance updates live. Use the `run` / `verify` skills to launch and observe.
5. **Defensive audit:** architecture test confirms no broker/order interface exists and `LiveTradingEnabled` cannot be true; boot log shows `DEFENSIVE MODE: analysis + paper only`.

---

## 13. Claude Code Skills & Sub-Agents (project automation layer)

**Why.** This is a 10-work-package, multi-discipline build. We encode the recurring ICT expertise and the project conventions as **project-scoped** Claude Code **skills** (`.claude/skills/`) and **sub-agents** (`.claude/agents/`), committed to the repo, so every future session — human or agent — executes consistently with the ICT rules and house style baked in. Project scope wins over user scope on a name clash. The orchestrator (main session) dispatches a WP to the matching sub-agent; each sub-agent pulls the relevant skill(s) for rules/procedure.

> **REQUIREMENT — everything project-scoped (not user-based):** ALL agents and skills live under the repo's `c:\Repos\Personal\ICT transcribe\.claude\` (`.claude/agents/`, `.claude/skills/`). **Nothing is written to `~/.claude/`.** Any agent that uses persistent memory sets `memory: project` (stored at `.claude/agent-memory/<name>/`, committed with the repo) — never `user`. This keeps the entire automation layer versioned and team-shared via git.

> **Format note (verified against current Claude Code):** sub-agents = a single `.claude/agents/<name>.md` with YAML frontmatter (`name`, `description` required; `tools`, `model`, `skills`, `effort`, etc. optional; omit `tools` to inherit all). Skills = `.claude/skills/<name>/SKILL.md` (directory name = `/command`); frontmatter `description` drives auto-invocation; `allowed-tools` pre-approves tools; bundle helper files in the skill dir and reference by name. `skills:` in an agent preloads a skill's full content at startup.

> **SUPERSEDE NOTE (architecture v2):** any "MediatR" / "vertical slice command-handler" / "LlmKit" wording in the staged skill/agent bodies below is **superseded by §3.0 (DDD) + §3.0a (modular monolith + in-memory `IMessageBus`, no MediatR) + the clean-code/resources rules**. The 7 agents + 3 skills already written to disk in the earlier step must be **scrubbed of MediatR/LlmKit** during execution: `vsa-slice-builder` → builds **module use-cases wired on the bus** (not MediatR); `add-vertical-slice` → module feature procedure on the bus; remove every LlmKit mention; add the resources/no-magic-string rule.

> **ICT CONFORMANCE GATE — "every change checks ICT concepts" (user requirement):** a new skill **`ict-conformance`** plus a settings hook make ICT fidelity a gate on every code change. Mechanism: (1) a `PostToolUse` hook on `Edit|Write` to `src/**` prints a reminder to run `/ict-conformance`; (2) a `Stop` hook blocks "done" until conformance is recorded for touched ICT files; (3) every WP's Definition-of-Done in §11 gains "**ICT conformance: change reviewed against §2.5/§2.5.10 by `ict-domain-expert` or `/ict-conformance`**". Detector/scan/paper-trade changes cannot be called complete without it.

### 13.1 Sub-agents to create (`.claude/agents/*.md`)
| Agent | Role | Maps to WP | Tools | Model | Preloads |
|---|---|---|---|---|---|
| `ict-domain-expert` | ICT methodology authority; authors rule specs, reviews detectors for fidelity to transcripts (read-only) | 1,3,9 | Read, Grep, Glob | opus | `ict-methodology` |
| `ict-detector-engineer` | Implements PURE domain detectors via strict TDD | 1 | Read, Write, Edit, Bash, Grep, Glob | opus | `ict-methodology`, `add-ict-detector` |
| `module-feature-builder` (was `vsa-slice-builder`) | Builds module use-cases wired on the in-memory bus (Scan/PaperTrades/Performance) — DDD inside, no MediatR | 3,4,5,6 | Read, Write, Edit, Bash, Grep, Glob | opus | `add-vertical-slice` |
| `ef-persistence-engineer` | EF Core entities/configs/JSONB/migrations | 2 | Read, Write, Edit, Bash, Grep, Glob | sonnet | — |
| `reqnroll-test-engineer` | Gherkin + Testcontainers + xUnit | 9 (+unit/integration) | Read, Write, Edit, Bash, Grep, Glob | opus | `ict-methodology` |
| `react-dashboard-builder` | React/TS dashboard + SignalR + Recharts | 8 | Read, Write, Edit, Bash, Grep, Glob | opus | — (invokes `frontend-design`) |
| `defensive-guardrail-auditor` | Read-only auditor enforcing the no-live-trading guardrail | all (pre-merge) | Read, Grep, Glob, Bash | sonnet | `defensive-guardrail-check` |
| `pr-reviewer` | PR gate: ICT conformance + .NET zero-warning clean build/no smells + React typecheck/lint + guardrail; APPROVE/REQUEST-CHANGES | every PR | Read, Grep, Glob, Bash | opus | `ict-methodology`, `ict-conformance`, `defensive-guardrail-check` |

Staged file contents (create verbatim on approval):

~~~~markdown
# .claude/agents/ict-domain-expert.md
---
name: ict-domain-expert
description: ICT (Inner Circle Trader) methodology authority. Use PROACTIVELY whenever code, tests, or detectors must faithfully encode an ICT rule (killzones, FVG, order blocks, liquidity sweeps, MSS/displacement, OTE, daily/weekly bias, SMT, the entry model). Authors the rule spec, reviews a detector for fidelity to the transcripts, and resolves domain ambiguities. Read-only advisor — never writes production code.
tools: Read, Grep, Glob
model: opus
skills:
  - ict-methodology
---
You are a senior ICT trader and the domain authority for this repository. Your single source of
truth is the `ict-methodology` skill plus the transcripts in `2022 ICT Mentorship/` and
`ICT Forex - Market Maker Primer Course/` and the plan at
`C:\Users\Mostafa\.claude\plans\system-role-you-are-an-binary-feather.md` (§2, §2.5, §4).

When invoked you will either (a) produce a precise, automatable rule spec for a detector/feature, or
(b) review existing logic for fidelity to the methodology. Always:
1. Cite the exact ICT rule and, where possible, the episode it comes from.
2. State the rule as deterministic IF/THEN conditions with EXACT numbers (NY session times, fib
   levels incl. 0.705 sweet spot, pip thresholds, risk %, min R:R). No vague language.
3. Flag any place the implementation diverges from the transcripts or invents a parameter.
4. Respect the defensive guardrail — analysis/paper only; never advise an execution path.
Output: a numbered spec or a prioritized review (Critical / Should-fix / Note). You do not edit files.
~~~~

~~~~markdown
# .claude/agents/ict-detector-engineer.md
---
name: ict-detector-engineer
description: Implements PURE C# domain detectors in IctTrader.Domain via strict TDD. Use when building or modifying any ISetupDetector (FairValueGap, OrderBlock, Liquidity, LiquiditySweep, Displacement, MarketStructureShift, AsianRange, Judas, OTE, Killzone, DailyBias, and long-tail PD-array detectors). Writes the failing xUnit test first, then the minimal pure implementation.
tools: Read, Write, Edit, Bash, Grep, Glob
model: opus
skills:
  - ict-methodology
  - add-ict-detector
---
You implement ICT detectors in `src/IctTrader.Domain` as PURE, deterministic functions: no I/O, no
`DateTime.Now` (inject `IClock`), no EF/MediatR/ASP.NET references. Follow the `add-ict-detector`
skill procedure exactly and the rules in the `ict-methodology` skill.

Discipline (non-negotiable):
1. RED — write a failing xUnit test in `tests/IctTrader.UnitTests` using a hand-built candle fixture
   that encodes the exact ICT condition (and its boundary/negative cases). Run it; see it fail.
2. GREEN — implement the smallest pure detector that passes. Reuse `MarketContext` windows and the
   existing primitives; do not duplicate swing/FVG logic.
3. REFACTOR — keep it clean; every magic number comes from an Options POCO, never a literal.
4. Register the detector in the pipeline and add its confluence weight + appsettings constant.
Always run `dotnet test tests/IctTrader.UnitTests` before reporting done; paste the passing output.
~~~~

~~~~markdown
# .claude/agents/vsa-slice-builder.md
---
name: vsa-slice-builder
description: Builds vertical-slice MediatR features (Features/Scan, Features/PaperTrades, Features/Performance, Features/Killzones) following the project's VSA + strict Options + LlmKit conventions. Use when adding or modifying an Application feature slice — command/handler/validator + minimal-API endpoint + slice test. NEVER introduces generic repositories or layer-based folders.
tools: Read, Write, Edit, Bash, Grep, Glob
model: opus
skills:
  - add-vertical-slice
---
You build features the Vertical Slice way: group by FEATURE under `IctTrader.Application/Features/<Name>`,
one MediatR command/query + handler + FluentValidation validator per slice, DTOs in `Contracts`,
and a thin minimal-API endpoint in the matching endpoint group. Handlers WIRE; the domain DECIDES —
put no business rule in a handler. Mirror `c:\Repos\Sase\LlmKit`: strict Options pattern (no magic
numbers), enum-driven provider selection + strategy factory, resilience via decorators. Follow the
`add-vertical-slice` skill. Build and run the slice's test before reporting done.
~~~~

~~~~markdown
# .claude/agents/ef-persistence-engineer.md
---
name: ef-persistence-engineer
description: EF Core + PostgreSQL specialist. Use when adding or altering entities, IEntityTypeConfiguration, JSONB columns, indexes, or generating migrations in IctTrader.Infrastructure. Knows the decimal-precision, timestamptz, JSONB, and idempotent-ingestion index conventions in plan §7.
tools: Read, Write, Edit, Bash, Grep, Glob
model: sonnet
---
You own persistence in `IctTrader.Infrastructure/Persistence`. Conventions (plan §7): prices
`numeric(18,8)`, currency `(18,2)`; enums as strings; UTC `timestamptz` everywhere; JSONB via
`.ToJson()`/`jsonb` for `Setup.Reason`/`Targets`/`Evidence`, tick/candle raw payloads, paper-trade
`Targets`/`Fills`; concurrency token `xmin`. Indexes: candles UNIQUE `(Symbol,Timeframe,OpenTimeUtc)`
for idempotent ingestion; setups `(Symbol,DetectedAtUtc DESC)`,`(Grade)`; paper trades
`(AccountId,Status)`,`(Symbol,Status)`,`(AccountId,Killzone)`; ticks `(Symbol,TimeUtc)` + GIN(payload).
Use code-first migrations from Infrastructure with Api as startup project; provide a design-time factory.
Verify with a round-trip integration test against Testcontainers Postgres.
~~~~

~~~~markdown
# .claude/agents/reqnroll-test-engineer.md
---
name: reqnroll-test-engineer
description: E2E/integration test specialist using Reqnroll (Gherkin), Testcontainers for .NET, and xUnit. Use when writing .feature files, step definitions, Testcontainers Postgres fixtures, the CustomWebApplicationFactory, ReplayMarketDataFeed/FakeClock doubles, or deterministic candle fixtures. Tests must be reproducible (same candles → same result).
tools: Read, Write, Edit, Bash, Grep, Glob
model: opus
skills:
  - ict-methodology
---
You write the mandatory test pyramid (plan §8). E2E uses Reqnroll to generate xUnit from `.feature`
files, Testcontainers to boot Postgres once per run, Respawn to reset between scenarios, and a
`CustomWebApplicationFactory<Program>` that swaps EF→container, `IMarketDataFeed`→`ReplayMarketDataFeed`,
`IClock`→`FakeClock`, and the alert notifier→a capturing double, with the background scanner disabled
(tests pump the pipeline). Build candle fixtures from named ICT anchors (Asian range → Judas sweep →
MSS displacement → bullish FVG in London killzone → OTE entry → target) — never magic numbers. Use ICU
id `America/New_York`, never the Windows `"Eastern Standard Time"`. Run the suite and paste green output.
~~~~

~~~~markdown
# .claude/agents/react-dashboard-builder.md
---
name: react-dashboard-builder
description: Builds the React + TypeScript dashboard in web/ict-dashboard — alerts feed, active paper-trades table, performance panel — with SignalR live updates, React Query server state, and Recharts equity curve. Use for any frontend work. Keeps web/ict-dashboard/src/types/api.ts byte-for-byte in sync with backend DTOs.
tools: Read, Write, Edit, Bash, Grep, Glob
model: opus
---
You build the dashboard (plan §9). React Query owns server state; SignalR (`/hubs/trading`) pushes
deltas merged via `setQueryData`. Types in `src/types/api.ts` mirror backend DTOs exactly (generate
from OpenAPI where possible). For the visual pass — dark trading-desk theme, tabular-numeral prices,
semantic colors (green long/win, red short/loss, amber pending), killzone badge colors — INVOKE the
`frontend-design` skill rather than guessing. Verify with `npm run typecheck` and `vitest`, then load
the app and confirm the three panels render and live-update.
~~~~

~~~~markdown
# .claude/agents/defensive-guardrail-auditor.md
---
name: defensive-guardrail-auditor
description: Read-only security/architecture auditor that enforces the NON-NEGOTIABLE no-live-trading guardrail. Use PROACTIVELY before any merge and after changes to Infrastructure/Trading, market-data feeds, options, or DI. Verifies ITradeExecutor has only SimulatedTradeExecutor, no broker/order-routing API exists, LiveTradingEnabled cannot be true, and feeds are read-only.
tools: Read, Grep, Glob, Bash
model: sonnet
skills:
  - defensive-guardrail-check
---
You are the guardian of the system's defensive posture (plan §0/§6.3). Run the
`defensive-guardrail-check` skill and report PASS/FAIL with evidence. You NEVER edit code — you only
read, grep, and run read-only checks/tests. Fail loudly (Critical) if you find: a second
`ITradeExecutor` implementation; any broker/order-routing symbol (order, buy, sell, placeOrder,
execute, fill-to-broker, OANDA trade endpoints, MT5 order send); a way for `LiveTradingEnabled` to be
true; a writable/non-sandbox feed; or a missing/failing architecture test. Recommend the fix; do not apply it.
~~~~

### 13.2 Skills to create (`.claude/skills/*/SKILL.md`)
| Skill | Purpose | Invocation |
|---|---|---|
| `ict-methodology` | Canonical, codifiable ICT rules + the mined entry model (single source of truth) | auto + `/ict-methodology` |
| `add-ict-detector` | Procedure to add a pure detector (TDD → register → weight → config) | `/add-ict-detector` |
| `add-vertical-slice` | Procedure to add a module use-case wired on the in-memory bus (DDD inside, no MediatR) | `/add-vertical-slice` |
| `mine-ict-transcripts` | (Re)run the sweep + entry-model mine workflows to refresh rules | `/mine-ict-transcripts` |
| `verify-ict-system` | Start app + replay fixture + confirm alert/trade/performance | `/verify-ict-system` |
| `defensive-guardrail-check` | The mechanical no-live-trading checklist (pre-merge + CI) | `/defensive-guardrail-check` |
| **`ict-conformance`** | **Gate every code change against the ICT model (§2.5/§2.5.10) — run on each change** | `/ict-conformance` |
| **`git-workflow`** | **Issue → branch → imperative commits (title+body, WHY-not-WHAT) → PR; the team's contribution convention (§14)** | `/git-workflow` |
| **`update-memory`** | **After each work session, refresh CLAUDE.md (## Status) + docs/PLAN.md so memory stays current** | `/update-memory` |

> **PR review gate + memory hygiene:** before `gh pr create`, the **`pr-reviewer`** agent reviews the branch (ICT conformance + .NET zero-warning clean build + no code smells + React typecheck/lint + guardrail) — fix all Critical/Should-fix first. After each work period, **`/update-memory`** updates CLAUDE.md + the plan. Two hooks in `.claude/settings.json` enforce both: PostToolUse `ict-conformance-reminder.ps1` and Stop `memory-update-reminder.ps1` (blocks the stop once while `src/`/`tests/`/`web/` changes are pending).

Staged file contents (create verbatim on approval):

~~~~markdown
# .claude/skills/ict-methodology/SKILL.md
---
name: ict-methodology
description: The canonical, codifiable ICT (Inner Circle Trader) trading rules this system encodes — killzone NY times, Asian range, Judas swing, AMD/Power-of-3, FVG, order blocks, liquidity sweeps, displacement/MSS, OTE fibs, daily/weekly bias, SMT, risk model, and THE mechanical intraday entry checklist mined from the 2022 Mentorship. Use whenever implementing, testing, or reviewing any detector, confluence rule, setup, or parameter, so the code matches the transcripts exactly.
---
# ICT Methodology — codifiable rules (single source of truth)
All times are **New York (America/New_York), DST-aware**; the financial day starts 00:00 NY.
Full detail + citations live in plan §2 / §2.5; the must-know parameters are below.

## Killzones (NY)
- Asian 19:00–00:00 (forms the Asian Range) · London Open 02:00–05:00 (highest odds of the day's
  high/low) · New York Open 07:00–09:00 (USD pairs; Silver Bullet 10:00–11:00) · London Close 10:00–12:00.
- Operator selects active killzones via `Ict:Scanning:ActiveKillzones` (default LondonOpen + NewYorkOpen).

## Primitives
- Swing high/low = 2 lower-highs / higher-lows either side.
- FVG (3-candle): bullish `c1.High < c3.Low`; bearish `c1.Low > c3.High`.
- Order block: last down-close before an up-move = bullish OB (key = its open); mirror for bearish.
- Liquidity: buy-side above old/equal highs, sell-side below old/equal lows. Sweep = wick beyond a
  level then close back inside.
- Displacement = energetic move creating an FVG; precondition for a valid MSS/entry.
- MSS/CHoCH = break of a short-term swing AFTER a liquidity sweep.

## Entry / bias / risk
- OTE = fib retrace of the impulse leg: entry 0.62–0.79, sweet spot **0.705**; stop at the swing
  extreme; targets at 0 (old high/low) and negative extensions -0.27, -0.62, -1.0. Min **2:1** R:R.
- Daily bias from prior-day high/low draw on liquidity; weekly low often Tue London → Wed NY (bull week).
- Risk 1–2% (max 3%); after a loss → 1%, 2nd loss → 0.5%, restore after 50% recovery; 5 wins → lowest unit.

## THE mechanical entry model (mined from the 2022 Mentorship — plan §2.5)
**ICT 2022 Intraday FVG Model — Liquidity Sweep → MSS/Displacement → PD-Array OTE Entry.**
Confirm a setup ONLY when ALL of these hold (RequiredConditions):
1. **Bias** — daily one-sided: discount (<50% of daily range) = bullish, premium (>50%) = bearish; neutral = NO trade. Trade only with bias.
2. **Draw on liquidity** — a valid opposing target exists (relative-equal H/L, prior-day H/L, HTF FVG, big figures 00/20/50/80).
3. **Killzone** — inside an enabled window (FX: London Open 02:00–05:00, NY 07:00–10:00, London Close 10:00–11:00, PM 13:30–16:00; Index: AM 08:30–11:00). NEVER lunch 12:00–13:00.
4. **Liquidity sweep** — price raids a prior high/low AGAINST the trade direction (Judas vs midnight/08:30 open).
5. **MSS + displacement** — an energetic candle breaks a prior 3-candle swing and CLOSES beyond it (weak/wick = NO trade).
6. **FVG** — first 3-candle gap found 15m→1m within the displacement leg (none → NO trade), in the correct half: shorts ≥50%, longs ≤50%.
7. **Entry** — limit in the FVG/OB at OTE **62–79%** (sweet-spot 70.5%, *Primer-sourced default*); OB entry = OB open +1 tick / +3 pips FX.
8. **Stop** — beyond swept swing / FVG / OB (1–2 ticks, ~10 pips FX); clear the farther of two stacked FVGs.
9. **Targets** — T1 nearest FVG/50%/short-term level (partial), T2 opposing liquidity / HTF draw; min RR ~2.5R (configurable). Trail 50%→25% risk, 75%→breakeven. Max hold 90–120 min, no overnight.

**Grade/alert gate:** score = Σmatched weights / Σapplicable ×100. A ≥80, B 65–79 (both need all RequiredConditions), C 50–64 (watchlist), Reject <50. **Alert floor 65.** Weights & full detail in plan §2.5.

**Caveats (verify medium-confidence, §2.5.7):** 70.5% sweet spot & Silver Bullet are Primer/canon, not grounded in these 2022 eps — keep configurable; FX vs index killzone windows are SEPARATE (instrument-class switch, default FX); ~2.5R is a default not a hard rule; quantify "displacement" with a body/ATR filter; detectors must emit FVG two-touch + ITH/ITL **invalidation**, not just formation.

## Guardrail
Analysis + paper trading ONLY. Never propose or implement a live-order path.
~~~~

~~~~markdown
# .claude/skills/add-ict-detector/SKILL.md
---
name: add-ict-detector
description: Step-by-step procedure to add a new PURE ICT detector to IctTrader.Domain — write the failing unit test first, implement the deterministic detector, register it in the scan pipeline, add its confluence weight, and surface every constant in appsettings. Use when extending the detector set (e.g. BreakerBlock, MitigationBlock, SMT divergence, Silver Bullet, Turtle Soup).
allowed-tools: Read Write Edit Grep Glob Bash(dotnet test *)
---
# Add an ICT detector (TDD)
1. **Spec** — get the exact rule from the `ict-methodology` skill / plan §2. Write it as IF/THEN with
   exact numbers. If ambiguous, consult the `ict-domain-expert` agent.
2. **RED** — in `tests/IctTrader.UnitTests/Detection/<Name>DetectorTests.cs`, build a hand-crafted
   candle fixture encoding the positive case + at least one boundary + one negative case. Run
   `dotnet test tests/IctTrader.UnitTests` and confirm it fails for the right reason.
3. **GREEN** — implement `<Name>Detector : ISetupDetector` in `src/IctTrader.Domain/Detection/Detectors/`.
   PURE: no I/O, no `DateTime.Now`, no infra refs. Reuse `MarketContext` windows + existing primitives.
   Return a `DetectorResult` with Direction, KeyLevel, ReasonFragment, Evidence.
4. **Constants** — every threshold comes from an Options POCO bound to `Ict:*` in appsettings. No literals.
5. **Register** — add the detector to the pipeline registration and give it a `ConfluenceCondition`.
6. **Weight + grade** — add its weight to `ConfluenceOptions`; adjust RequiredConditions if it's mandatory.
7. **REFACTOR + verify** — re-run the unit suite; paste the passing output. Detector stays deterministic.
~~~~

~~~~markdown
# .claude/skills/add-vertical-slice/SKILL.md
---
name: add-vertical-slice
description: Procedure to add a MediatR vertical slice the project's VSA way — Features/<Name>/ with Command/Query + Handler + Validator, DTOs in Contracts, a thin minimal-API endpoint in its group, and a slice test. Use when adding any new Application feature. Enforces group-by-feature and forbids generic repositories / layer folders.
allowed-tools: Read Write Edit Grep Glob Bash(dotnet *)
---
# Add a vertical slice
1. Create `src/IctTrader.Application/Features/<Name>/` — one folder per FEATURE (never by layer).
2. Define the MediatR `record <Name>Command/Query : IRequest<TResult>` + its `Handler`. Handler WIRES
   (calls domain + abstractions); ALL business rules stay in `IctTrader.Domain`.
3. Add a `FluentValidation` validator; it runs via the pipeline behavior.
4. Put request/response DTOs in `Application/Contracts` (camelCase JSON). If they cross to the frontend,
   keep `web/ict-dashboard/src/types/api.ts` in sync.
5. Add a thin endpoint in the matching minimal-API endpoint group in `IctTrader.Api` — it only sends the
   MediatR message and maps the result. No logic.
6. Wire any new abstraction with a single DI line (strategy-factory style; mirror LlmKit). No generic repo.
7. Test: a focused slice test (integration against Testcontainers Postgres if it touches the DB). Run it green.
~~~~

~~~~markdown
# .claude/skills/mine-ict-transcripts/SKILL.md
---
name: mine-ict-transcripts
description: How to (re)run the ICT transcript analysis workflows — the broad 65-transcript sweep and the focused 2022-Mentorship entry-model mine — to extract or refresh the codifiable rules catalog when the domain rules need updating or a concept may have been missed.
---
# Mine the ICT transcripts
Each course folder also has a combined `_<Playlist> - FULL PLAYLIST.txt`; individual per-episode `.txt`
files are best for parallel fan-out. Two saved workflows exist (agents cache → cheap to resume):

- **Focused entry-model mine (2022 Mentorship → THE setup):**
  `Workflow({ scriptPath: "C:\Users\Mostafa\.claude\projects\C--Repos-Personal-ICT-transcribe-2022-ICT-Mentorship\77104297-4990-41a9-b598-0603dfc9c8e4\workflows\scripts\mentorship-entry-model-mine-wf_7f702dda-09a.js", resumeFromRunId: "wf_7f702dda-09a" })`
- **Broad 65-transcript taxonomy sweep:**
  `Workflow({ scriptPath: "C:\Users\Mostafa\.claude\projects\C--Repos-Personal-ICT-transcribe\77104297-4990-41a9-b598-0603dfc9c8e4\workflows\scripts\ict-transcript-sweep-wf_b6717b76-098.js", resumeFromRunId: "wf_b6717b76-098" })`

After a run: paste the consolidated rules into plan §2.5 + the `ict-methodology` skill, and extend the
detector list (§4.2) + `ConfluenceOptions` weights with any newly surfaced concept.
~~~~

~~~~markdown
# .claude/skills/verify-ict-system/SKILL.md
---
name: verify-ict-system
description: End-to-end manual verification of the running ICT system — start Postgres + API + dashboard, replay a known candle fixture, and confirm an alert fires with correct ICT reasoning, a paper trade opens and closes, and performance metrics update. Use to validate a change in the real app (not just unit tests).
allowed-tools: Read Grep Glob Bash(docker *) Bash(dotnet *) Bash(npm *)
---
# Verify the ICT system end-to-end
1. `docker compose up -d postgres` (or let Testcontainers handle test runs).
2. `dotnet run --project src/IctTrader.Api` — confirm the boot log prints `DEFENSIVE MODE: analysis +
   paper only` and startup validation passes (LiveTradingEnabled=false).
3. Point the feed at the **Replay** provider loaded with the `BullishLondonKillzone` fixture (or OANDA
   practice for live smoke).
4. `cd web/ict-dashboard && npm run dev`; open the dashboard.
5. Confirm: an alert appears with a reasoning string like "Bullish FVG … inside London Open Killzone
   after Asian High sweep, MSS confirmed, OTE 0.705"; a paper trade opens then closes at target; the
   performance panel updates (win rate, R:R, drawdown, equity curve).
6. If anything is off, prefer the `run` / `verify` built-in skills to drive and observe the app.
~~~~

~~~~markdown
# .claude/skills/defensive-guardrail-check/SKILL.md
---
name: defensive-guardrail-check
description: The mechanical checklist that proves the no-live-trading guardrail holds — only SimulatedTradeExecutor implements ITradeExecutor, no broker/order-routing symbol exists anywhere, LiveTradingEnabled validates false at startup, and all market-data feeds are read-only/sandbox. Use before merging and in CI.
allowed-tools: Read Grep Glob Bash(dotnet test *)
---
# Defensive guardrail check (must all PASS)
1. **Single executor** — exactly one `ITradeExecutor` implementation, `SimulatedTradeExecutor`, and it
   writes only to our DB. (grep for `: ITradeExecutor`.)
2. **No order-routing API** — no broker order/buy/sell/placeOrder/execute-to-broker symbols; no OANDA
   `orders`/`trades` POST endpoints; MT5 bridge exposes only subscribe + inbound tick/bar (no order send).
3. **LiveTradingEnabled** — defaults false; an `IValidateOptions<>` FAILS startup if ever true. Confirm
   the validator exists and is registered.
4. **Read-only feeds** — every `IMarketDataFeed` is `IsReadOnly = true`; credentials sandbox/practice only.
5. **Architecture test** — the CI architecture test asserting 1–4 exists and passes:
   `dotnet test --filter Category=Architecture`.
6. **Advisory flag** — every `Setup` carries `IsAdvisoryOnly = true`; the SignalR contract has no
   "execute" message.
Report PASS/FAIL per item with file:line evidence. Any FAIL blocks merge.
~~~~

~~~~markdown
# .claude/skills/ict-conformance/SKILL.md
---
name: ict-conformance
description: Gate EVERY code change against the ICT model. Use after writing/modifying any detector, confluence rule, killzone/session logic, bias, paper-trade sizing/targets, or execution-cost code — verify it still matches the transcript-mined entry model (plan §2.5) and the web cross-check resolutions (§2.5.10), with exact parameters from Options/Resources (no magic numbers/strings). Run on each change before calling it done.
allowed-tools: Read Grep Glob Bash(git diff *)
---
# ICT conformance check (run on every change)
!`git diff --stat`

For each changed file touching trading logic, verify against `ict-methodology` + plan §2.5/§2.5.10:
1. **Rule fidelity** — does the change encode the ICT rule exactly? Cite the §2.5 step / episode. No invented parameters.
2. **Contested-point defaults respected** (§2.5.10): OTE fib **body-anchored** by default (wicks only FOMC/NFP); min RR configurable ~2.5R (not 3); London Close 10:00–11:00; lunch 12:00–13:00; SD targets −1/−1.5/−2; Asian 19:00–00:00; FX NY 07:00–10:00.
3. **Provenance flags** — 70.5% sweet spot, Silver Bullet, PD 7-tier ranking, quadrants = Primer/secondary defaults, configurable, NOT presented as Mentorship-verbatim. No hard-coded win-rate stats.
4. **No magic numbers** → Options POCO (`Ict:*`); **no magic strings** → `.resx`.
5. **Determinism + DDD** — logic in the domain (pure), not a handler; detector emits invalidation, not just formation.
6. **Guardrail** — nothing introduces a live-order path.
Output: per-file PASS / NEEDS-FIX with the specific §2.5 reference. For non-trivial domain changes, escalate to the `ict-domain-expert` agent. NEEDS-FIX blocks "done".
~~~~

**Settings hook (`.claude/settings.json`) wiring the gate** (created on approval):
```jsonc
{ "hooks": {
  "PostToolUse": [ { "matcher": "Edit|Write",
    "hooks": [ { "type": "command",
      "command": "pwsh -NoProfile -File .claude/hooks/ict-conformance-reminder.ps1" } ] } ]
} }
```
The script checks whether the edited path matches `src/**` trading code and, if so, emits a reminder to run `/ict-conformance` (and records that the touched ICT files still need a conformance pass). Keep it advisory-but-loud; the §11 DoD makes it mandatory.

~~~~markdown
# .claude/skills/git-workflow/SKILL.md
---
name: git-workflow
description: The team's Git/GitHub contribution workflow — open a GitHub issue first, branch as feature/#<issue>-<title>, write imperative commit titles "#<issue> Add X" (< 72 chars) with an 80-column-wrapped body explaining WHY (not what), and open a PR that states the issue and the fix. Use for every code change that will be committed, pushed, or PR'd.
allowed-tools: Read Grep Glob Bash(git *) Bash(gh *)
---
# Git / GitHub contribution workflow (follow for EVERY change)

## 1. Issue first (it owns the number)
Every change begins from a GitHub issue — its number `N` is used in the branch, commits, and PR.
Create one if it doesn't exist: `gh issue create --title "<imperative summary>" --body "<the problem +
the intended outcome>"`. Capture the returned number.

## 2. Branch
Branch off the default branch: `git switch -c <type>/#N-<kebab-title>` where `type` ∈
`feature | fix | refactor | chore` (default `feature`).
Example: `feature/#42-trade-style-timeframe`.

## 3. Commits — title + body, imperative, WHY not WHAT
- **Title** (HARD ≤ 72 chars): `#N <Verb> <subject>`. The verb is an imperative-MOOD command —
  **Add, Refactor, Fix, Remove, Update, Rename, Move, Extract, Introduce**… NEVER past tense ("Added")
  or gerund ("Adding"). Example: `#42 Add Trade domain`.
- **Body** (after a blank line): prose **hard-wrapped at 80 columns** that explains **WHY** the change was
  made — the motivation, the context, the problem it solves. The diff already shows WHAT changed, so do
  not narrate the code. Reference the issue where useful.
- **Trailer** (last line): `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- One logical change per commit. Use a heredoc for the multi-line message:
```bash
git commit -m "#42 Add Trade domain" -m "$(cat <<'EOF'
The scanner needs a place to express position lifecycle and risk rules that is
independent of persistence and transport, so paper-trade behaviour can be unit
tested deterministically and the live-trading guardrail stays structural.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

## 4. Pull request — what was the issue, how we fixed it
`gh pr create` with title `#N <Verb> <subject>`. Body has two sections:
- **Issue** — what was wrong or what was needed (link `Closes #N`).
- **Fix** — how we addressed it (approach + notable decisions) and how to verify.
- Last line: `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

## Guardrails
Commit/push only when the user asks; if on the default branch, branch first. Never commit secrets, never
force-push shared branches, never skip hooks (`--no-verify`) unless explicitly asked. Run the
`defensive-guardrail-check` + `/ict-conformance` before opening a PR that touches trading logic.
~~~~

### 13.3 How they interlock
- `ict-methodology` is the **one** rules source; the domain/detector/test agents preload it so rules never drift between detectors, tests, and docs. It is refreshed by `mine-ict-transcripts`.
- Orchestration: main session reads the plan → dispatches each WP to its agent (e.g. WP1 → `ict-detector-engineer`, WP2 → `ef-persistence-engineer`, WP8 → `react-dashboard-builder`), then runs `defensive-guardrail-auditor` before any integration/merge.
- `superpowers:test-driven-development` and the built-in `frontend-design` / `run` / `verify` skills compose with these — the project skills add the ICT- and VSA-specific procedure on top.
- **DDD is enforced by the automation layer:** `add-vertical-slice` mandates rich aggregates / value objects / domain services / domain events and bans anemic models + generic repos; `add-ict-detector` keeps every detector a pure domain service; `ict-detector-engineer` and `vsa-slice-builder` carry that discipline; `ict-domain-expert` reviews fidelity to both the methodology (§2.5) and the model (§3.0).

### 13.4 Build/execution status (as of this plan)
- ✅ **Created on disk (need an architecture-v2 scrub):** all 7 sub-agents; 3 skills (`ict-methodology`, `add-ict-detector`, `add-vertical-slice`). These still mention **MediatR/LlmKit** and must be edited on resume to: drop MediatR → in-memory bus; drop LlmKit; add modular-monolith + resources/no-magic-string rules; `ict-methodology` already DDD/§2.5-aware (refresh with §2.5.10).
- ⏳ **Pending (resume after approval):**
  1. Scrub the 3 existing skills + 7 agents of MediatR/LlmKit (above).
  2. Create 6 more skills: `mine-ict-transcripts`, `verify-ict-system`, `defensive-guardrail-check`, **`ict-conformance`**, **`git-workflow`**, **`update-memory`**.
  3. Create the **`pr-reviewer`** agent (PR review gate).
  4. Create `.claude/settings.json` + `.claude/hooks/ict-conformance-reminder.ps1` (PostToolUse ICT gate) + `.claude/hooks/memory-update-reminder.ps1` (Stop memory gate).
  5. Refresh `CLAUDE.md` from Appendix A (modular monolith, no-MediatR/LlmKit, resources, trade-realism, ICT gate, review gate + memory hygiene, mining-done).
  6. Then §14: `.gitignore` + `docs/PLAN.md` + `README.md` → `git init` + commit → `gh repo create ict-trading-analysis --private --source=. --remote=origin --push`.

> **STATUS UPDATE (executed):** all of the above are DONE — repo live at `MostafaEsmaeili/ict-trading-analysis` (private). 8 agents + 10 skills + both hooks created; genesis + conformance-hook + this review/memory batch committed. Application code (WP0+) proceeds via issue → branch → `pr-reviewer` → PR.

> **WP0 AMENDMENTS (these refine the plan; supersede the inline mentions where they differ):**
> 1. **Time abstraction = the BCL `TimeProvider`**, NOT a custom `IClock`. Inject `TimeProvider`
>    (`TimeProvider.System` in prod, `FakeTimeProvider` in tests); `NyClock` (WP1) wraps it over the IANA
>    id `America/New_York`. Everywhere this plan says `IClock`, read `TimeProvider`.
> 2. **Architecture tests are reflection-based** (assembly-reference inspection in
>    `IctTrader.ArchitectureTests`), not ArchUnitNET — dependency-free and already green; they assert the
>    same boundaries (SharedKernel/Domain depend on nothing internal; modules reach others only via
>    `*.Contracts`; no MediatR/commercial-test libs in production). ArchUnitNET may be layered in later for
>    type-level rules. Everywhere this plan says "ArchUnitNET-enforced", read "architecture-test-enforced".
> 3. **Dependency policy — latest stable, license-aware:** every NuGet **and** npm package is pinned to its
>    newest stable release, EXCEPT where the latest is commercially licensed — then pin the newest free/OSS
>    version and note why. Applied: **FluentAssertions 7.2.2** (8+ is commercial), MediatR avoided entirely,
>    ArchUnitNET dropped. Central package management is off (versions in each `.csproj`).
> 4. **Build/repo facts:** `.slnx` solution format; **22 projects**; LF line endings via `.gitattributes`
>    (so `dotnet format` `end_of_line = lf` is clean on Windows dev + Linux CI); `Killzone` enum is
>    `{ None, Asian, LondonOpen, NewYorkOpen, LondonClose }` (PM added later if WP1 needs it); module
>    Application/Infrastructure are empty shells in WP0; aggregate-coupled abstractions (`ITradeExecutor`,
>    `IRiskManager`, `IFillEvaluator`, repositories) land with their aggregates in WP4/WP5, not WP0.

---

## 14. Initialize git + publish to GitHub (`gh`)

The repo is currently NOT under version control. After the `.claude/` agents+skills and `CLAUDE.md` are created, initialize git and push to a new GitHub repo in the user's account via the `gh` CLI.

**Decisions (confirmed with user):** repo name **`ict-trading-analysis`**, **private** visibility, first push includes **everything** (incl. `.raw/` VTTs, cleaned transcripts, `CLAUDE.md`, plan snapshot, `.claude/` automation layer). Account: `MostafaEsmaeili` (gh 2.83.0, authenticated, `repo` scope present).

**Steps (run on approval, from `c:\Repos\Personal\ICT transcribe`):**
1. **`.gitignore`** — create one covering .NET (`bin/`, `obj/`, `*.user`), Node (`node_modules/`, `dist/`), env files (`.env`, `appsettings.*.Local.json`, any feed credentials), and OS noise. `.raw/` and the `.txt` transcripts ARE tracked (per the user's "everything" choice); never commit secrets.
2. **Snapshot the plan into the repo** — copy the plan to `docs/PLAN.md` so the source-of-truth blueprint is versioned alongside the code (the canonical copy stays at `~/.claude/plans/...`).
3. `git init -b main` → `git add -A` → **genesis commit** on `main` (the repo must exist before issues/PRs can). Follow the `git-workflow` commit style: imperative title `Add ICT trading-analysis scaffold, plan, and Claude automation` (≤ 72 chars), an 80-col body explaining WHY (establishes the defensive paper-trading baseline + versioned blueprint), ending with the `Co-Authored-By:` trailer.
4. **Create + push:** `gh repo create ict-trading-analysis --private --source=. --remote=origin --push`. Then print the repo URL.
5. **Guardrail before publish:** run the `defensive-guardrail-check` mindset on what's being pushed — confirm no API tokens / practice-account credentials / `.env` are staged. Feed credentials must NEVER be committed (they belong in user-secrets / environment).
6. Add a short root `README.md` (1-paragraph overview + the non-negotiable paper-only guardrail + link to `docs/PLAN.md`).

### 14.1 Ongoing contribution flow (the `git-workflow` skill — applies from here on)
Once the repo exists, **all** subsequent work (starting with WP0) goes through the convention encoded in the `git-workflow` skill:
1. **Issue first** — `gh issue create` for each unit of work; it owns the number `N`.
2. **Branch** — `feature/#N-<kebab-title>` (or `fix|refactor|chore`).
3. **Commits** — title `#N <ImperativeVerb> <subject>` (≤ 72 chars, command mood — Add/Refactor/Fix…, never "Added"); body wrapped at 80 cols explaining **WHY not WHAT**; `Co-Authored-By:` trailer.
4. **PR** — `gh pr create`, body = **Issue** (what was wrong, `Closes #N`) + **Fix** (how we addressed it + how to verify); ends with the Claude Code line. Run `defensive-guardrail-check` + `/ict-conformance` before opening.
The genesis commit (step 3 above) is the only commit exempt from the issue-number prefix, since no issue can exist before the repo does.

---

## Appendix A — `CLAUDE.md` to create at project root (`c:\Repos\Personal\ICT transcribe\CLAUDE.md`)

> Staged here because plan mode only permits editing this plan file. On resume, copy everything between the fences into `c:\Repos\Personal\ICT transcribe\CLAUDE.md` verbatim.

```markdown
# ICT Automated Trading-Analysis System

## What this is
A **defensive, paper-trading-only** system that translates the ICT (Inner Circle Trader)
methodology — extracted from the course transcripts in this repo — into an automated
market scanner, alerter, internal paper-trading simulator, and performance tracker.

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
- `2022 ICT Mentorship/` — 41 cleaned `.txt` transcripts.
- `ICT Forex - Market Maker Primer Course/` — 24 cleaned `.txt` transcripts.
- `build_transcripts.py` — converts `.raw/*.vtt` → cleaned `.txt` (per-video + combined playlist).
- `src/`, `tests/`, `web/` — the system (created during implementation; see the plan).

## The plan (source of truth)
Full implementation plan: `C:\Users\Mostafa\.claude\plans\system-role-you-are-an-binary-feather.md`
Read it before working. It contains: the ICT domain rules being coded, the VSA solution
structure, scaffolding commands, the two core features (ScanMarketData, ExecutePaperTrade),
the data-feed/MT5 design, the Gherkin+Testcontainers test strategy, the React dashboard,
and a dependency-ordered 10-agent work-package table.

## Tech stack (fixed)
.NET 10 C# Web API · **Modular Monolith** (feature modules decoupled behind an in-memory `IMessageBus`;
**NO MediatR** — it is commercially licensed) · **DDD** core · group by MODULE then FEATURE, no generic
repositories · PostgreSQL + EF Core (JSONB) · SignalR · React + TypeScript (Vite) · E2E tests with
Reqnroll (Gherkin) + Testcontainers for .NET + xUnit. **Clean Code: no magic numbers (Options) / no
magic strings (`.resx` resources).**

## Project conventions
- **Self-contained — do NOT depend on any sibling repo.** Minimal-API hosting; Clean Code + SOLID;
  strict Options pattern (every ICT/trading constant — killzone times, pip sizes, fib levels, risk %,
  spread/commission/slippage/swap — lives in appsettings, NO magic numbers); **NO magic strings — all
  human-facing/log/alert/validation/reason text in `.resx`**; enum-driven provider selection with a
  strategy-factory ("new provider = new impl + one DI line"); resilience via decorators.
- **Modular monolith (plan §3.0a):** modules talk ONLY via the in-memory bus + each other's `*.Contracts`;
  no module→module internal references (ArchUnitNET-enforced); swappable to a distributed bus later.
- `Directory.Build.props`: `net10.0`, `<Deterministic>true</Deterministic>`, nullable enable,
  warnings-as-errors, **`<InvariantGlobalization>false</InvariantGlobalization>`** (ICU must be present so
  `America/New_York` resolves on any host — §4.8).
- Reference direction: `SharedKernel`/`Domain` depend on nothing; modules → `SharedKernel` + `Domain` +
  others' `*.Contracts`; `Host` → all modules.
- **Trade-ready realism (plan §5.4):** paper P&L is booked net of spread, commission, slippage, and swap
  via `IExecutionCostModel`; intrabar fills use Open→Low→High→Close so wick-sweeps fill stops honestly.
- **ICT conformance gate:** every change is checked against the ICT model (§2.5/§2.5.10) via the
  `ict-conformance` skill + `ict-domain-expert`; a PostToolUse hook reminds, the §11 DoD makes it mandatory.
- **Git/GitHub workflow (the `git-workflow` skill):** every change starts from a GitHub **issue** (number `N`);
  branch `feature/#N-<title>`; **commit title** `#N <ImperativeVerb> <subject>` (≤ 72 chars, command mood —
  Add/Refactor/Fix…, never "Added"); **commit body** wrapped at 80 cols explaining **WHY, not WHAT**, ending
  with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`; **PR** body = *Issue* (what was
  wrong, `Closes #N`) + *Fix* (how we solved it + how to verify). Commit/push only when asked; branch off the
  default first; never commit secrets.
- **DDD is the core discipline (see plan §3.0):** ALL business logic lives in `IctTrader.Domain` —
  rich aggregates with invariants (`PaperTrade`, `PaperAccount`, `Setup`, `ScanSession`), self-validating
  value objects (`Price`, `Pips`, `OteZone`, `RiskPercent`…), domain services (`SetupScorer`,
  `IRiskManager`, `IFillEvaluator`, `PerformanceCalculator`, every `ISetupDetector`), and domain events
  (`SetupConfirmed`, `PaperTradeOpened/Closed`). **No anemic models. No generic repository** — repositories
  are aggregate-scoped interfaces in the Domain (`ISetupRepository`, `IPaperTradeRepository`). One bounded
  context, one ubiquitous language (the ICT terms in §2.5). VSA organizes use-cases; DDD models the inside.
- `IctTrader.Domain` is PURE (no EF/bus/ASP.NET) so aggregates + detectors are deterministic + unit-testable.
- **Time-zone aware (the host may run anywhere — §4.8):** UTC is the only source of truth; never
  `DateTime.Now`/`DateTimeOffset.Now`/`TimeZoneInfo.Local`/the ambient process zone — inject `IClock`.
  ALL NY-session math goes through the DST-aware `NyClock` using the ICU IANA id `America/New_York`
  (never the Windows id `"Eastern Standard Time"`); a startup validator fails fast if it can't resolve.
  Killzone classification is identical whether the server runs in UTC, Tokyo, or Berlin; the dashboard
  shows NY time by default.
- Handlers WIRE (orchestrate); domain DECIDES (no business rule in a handler).

## Common commands (once scaffolded)
- Build:        `dotnet build`
- Unit tests:   `dotnet test tests/IctTrader.UnitTests`
- E2E tests:    `dotnet test tests/IctTrader.E2E`   (needs Docker for Testcontainers)
- Run API:      `dotnet run --project src/IctTrader.Api`
- Run web:      `cd web/ict-dashboard && npm run dev`
- EF migration: `dotnet ef migrations add <Name> --project src/IctTrader.Infrastructure --startup-project src/IctTrader.Api`
- Rebuild transcripts: `python build_transcripts.py <raw_dir> <out_dir> "<Playlist Title>"`

## Build order (see plan §11)
WP0 contracts/skeleton → freeze contracts → then WP1 detectors / WP2 persistence / WP8 frontend
in parallel → WP3 scan slice; WP4→WP5→WP6 trading chain → WP7 feeds+host+SignalR → WP9 E2E gate.
Critical path: 0 → 2 → 4 → 5 → 6 → 7 → 9.

## Domain analysis status — DONE (mined)
Both courses are mined. The **24-episode Market Maker Primer** gives the framework (plan §2.1–2.4).
The **41-episode 2022 Mentorship** (the MAIN course) has been mined into **THE mechanical entry model**
— *ICT 2022 Intraday FVG Model: Liquidity Sweep → MSS/Displacement → PD-Array OTE Entry* — now in
**plan §2.5** (9-step checklist, RequiredConditions, weighted confluences, grade/alert gate, exact
params, full detector set, and §2.5.7 fidelity caveats). Encode §2.5 into `ConfluenceOptions` +
the detector set during WP1.

Saved workflows (resumable, agents cached): entry-model mine →
`...\mentorship-entry-model-mine-wf_7f702dda-09a.js` (resume `wf_7f702dda-09a`); broad 65-transcript
sweep → `...\ict-transcript-sweep-wf_b6717b76-098.js` (resume `wf_b6717b76-098`). Re-run to refresh rules.

## Selectable killzone (requirement)
The operator chooses which killzone(s) the scanner hunts in, via `Ict:Scanning:ActiveKillzones`
in appsettings (any subset of `Asian | LondonOpen | NewYorkOpen | LondonClose`). The
`InKillzone` confluence only matches inside an enabled killzone. ICT's stated preference:
**London Open** (highest odds of making the day's high/low) and **New York AM** (prime for USD
pairs; home of the Silver Bullet 10:00–11:00 NY). Default `["LondonOpen","NewYorkOpen"]`. The
dashboard exposes a killzone toggle.

## Status
Planning complete; 2022 Mentorship mined into §2.5. Automation layer (`.claude/agents/`, `.claude/skills/`)
being created. No application code yet. Resume by: (1) reading the plan (esp. §2.5 + §3.0 DDD), (2) starting
Work Package 0 (contracts + skeleton), then WP1 detectors encoding §2.5.
```

## Critical files (when implementation starts)
- `src/IctTrader.Domain/Detection/MarketContext.cs` + `Detectors/*` — the rolling-window state + pure ICT detectors (heart of the scanner).
- `src/IctTrader.Domain/Confluence/SetupCandidate.cs` + `SetupScorer.cs` — confluence FSM + grading gate (defines "high-probability").
- `src/IctTrader.Application/Features/Scan/ScanMarketDataHandler.cs` — composes Setup + reasoning, persists, alerts.
- `src/IctTrader.Application/Features/PaperTrades/ExecutePaperTradeHandler.cs` + `Domain/PaperTrade.cs` + `IctRiskManager.cs` — sizing/risk/lifecycle.
- `src/IctTrader.Infrastructure/Scanning/SymbolScanner.cs` + `Trading/FillSimulationService.cs` — stateful hosts.
- `src/IctTrader.Application/Contracts/Dtos.cs` + `Abstractions/*` — the frozen contracts.
- `tests/IctTrader.E2E/Features/IctSetupPipeline.feature` + `Steps/*` + `Fixtures/BullishLondonSetupFixture.cs` — the acceptance gate.
