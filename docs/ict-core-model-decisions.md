# ICT Core-Model Decisions Register

Transcript-cited resolutions of the contested §2.5 core-model questions (the audit's foundational
orders 1–2). Produced by the `ict-core-model-decisions` workflow (run `wf_de67d483-8be`, 4
`ict-domain-expert` resolvers + synthesis, code-verified against `src/`) and the
`ict-domain-completion-audit` (run `wf_25901e98-7f3`). **Rule: Mentorship-primary, web/Primer-secondary;
on any conflict the transcript-mined model wins.** Every numeric default stays configurable and
provenance-flagged where not verbatim.

Each decision is **pinned**: dependent detector/target/risk slices cite the decision id (e.g. `EG-1`)
rather than re-litigating. Status legend: **CODE-READY** (touches shipped logic now) · **ADDITIVE-FLAG**
(a config knob, default path unchanged) · **STATUS-QUO** (already correct) · **DEFERRED** (no shipped
surface yet) · **DONE** (already implemented in a merged slice).

---

## Entry geometry

- **EG-1 — OTE band.** Canonical band = **0.62–0.79 of the displacement leg, inclusive at both edges**,
  anchored **body-to-body** (wicks only on FOMC/NFP). `0.705` is a **Primer-sourced** preferred-depth
  tie-break ONLY (it appears in **zero** 2022 Mentorship episodes — grep-confirmed; only in Primer Ep18),
  flagged `isPrimerSourcedDefault`, never a hard gate or the sole entry. The Ep41 62–70 narrowing stays an
  opt-in `UseEp41Variant=false`. Cite: Mentorship Ep19/22/38, **Ep41 L8249** ("for optimal trade entry I like to
  use the bodies, lowest open or close in the swings") + **L8375-8380** (FOMC → "use the wick"); Primer Ep18.
  **DONE (issue #57).** The leg was wick-anchored (`current.Low/High`) — a real fidelity gap. Fixed:
  `LegAnchorMode {BodyToBody(default),WickToWick}` + `WickAnchorOnFomcNfp` (default on, NY-date-keyed, fail-open
  to body, fires only on a `CalendarEventType.Fomc|Nfp` day) live on **`DisplacementOptions`** (the leg owns its
  anchor — **deviation from the register's original `OteOptions` placement**, because the leg is built in
  `DisplacementDetector` and the OTE/equilibrium/SD targets all inherit the one anchor; `OteEntryResolver` is
  anchor-agnostic and unchanged). Default flips wick→**body** (`origin=Open`, `terminus=Close`, both directions),
  so the OTE entry AND `Displacement.EquilibriumPrice` (the FVG/OB correct-half, **option b**) move together; the
  daily-range **veto** frame (EG-2) reads `DailyRange`, not the leg, so it is structurally untouched. The
  leg-retrace **invalidation** reference also moves with the anchor (a close back through `leg.Origin` = the body
  Open by default, the wick Low under `WickToWick`) — coherent with the body leg, locked by a discriminating test.
  Edge-inclusivity + 0.705-preference locked. The multi-candle generalization (TIME-11-12, DONE) honors this: the
  body anchor selects `max(Open,Close)` of the terminus candle / `min(Open,Close)` of the origin candle across the
  leg — not the literal first-Open/last-Close.
- **EG-2 — Premium/discount anchor (load-bearing).** Step-1 (daily dealing-range 50%, the bias + entry-half
  **veto**) and step-6 (displacement-leg 50%, **which FVG is eligible**) are **two distinct reference
  frames** and must never be conflated. The daily-range frame stays the **single `PremiumDiscountHalf`
  weight (0.85)**; the leg-half is already a **precondition of `FvgPresent`/`OteZone`**, NOT a second
  weight. **Σ(applicable) stays 9.75** — which TGR-4's grading arithmetic depends on. If the leg-half is
  ever promoted to its own scored confluence it MUST be a new `ConfluenceCondition` (re-tune the
  denominator). Cite: Mentorship Ep41/06/19. **STATUS-QUO** (already correct in code).
- **EG-3 — Close-proximity entry.** A real, named, configurable entry rule, **OFF by default** (faithful
  baseline = a resting limit at the level). When on, fill at the **actual touched price** within a
  tolerance (INVENTED, flagged) of the array boundary on the correct PD side, still inside 62–79. Cite:
  Mentorship Ep10/29/07/22/35 (taught) + Ep09 (don't-chase). **DEFERRED** (rides the entry-orchestrator chain).

## Fair Value Gap semantics (four distinct constructs — never conflate)

- **FVG-SEM-1a — Two-touch void.** A "touch" = price **trading INTO** the gap (wick-into, bar High/Low,
  inclusive), NOT close-into and NOT full-fill. `VoidOnTouchCount=3` (Ep38-verbatim). Add a
  `TouchSemantics {WickInto(default), CloseInto}` flag. Cite: Mentorship Ep38. **ADDITIVE-FLAG** (detector
  already registers wick-into touches).
- **FVG-SEM-1b — Formation = touch 0.** The 3-candle displacement that creates the gap is not a return;
  counting starts at the FIRST retrace-in (the §2.5.1-step-7 entry retrace = touch 1). **STATUS-QUO + test**
  (code already seeds 0 on formation).
- **FVG-SEM-2 — Strict-first-FVG.** Step timeframes high→low and select the **FIRST** 3-candle gap in the
  leg; exactly one `IsSelectedEntryFvg`. "Stacked" = arm at the closer gap, size the stop to survive a stab
  into the farther, and **NIX the trade if the farther gap is hit first** (a `EntryCancelReason`). Cite:
  Mentorship Ep3:376–394. **CODE-READY** (add `StrictFirstFvg`, `IsSelectedEntryFvg`, the wrong-order nix).
- **FVG-SEM-3 — Validity exclusions = FLAG-ONLY.** The five web exclusions (no-sweep / Asian-range /
  counter-bias / no-CHoCH / overlapping-wicks) are §2.5.10 secondary additions: emit the FVG with exclusion
  evidence (`ApplyValidityExclusions=false`); the genuine vetoes are ALREADY RequiredConditions. The
  **Asian-range exclusion is dropped** as a default; **Asian becomes a selectable, deprioritized entry
  killzone** (Mentorship Ep10 trades it; Ep18 says ignore the Asian-*range* sub-model, not exclude FVGs by
  time). **ADDITIVE-FLAG**.
- **FVG-SEM-4 — Mitigation = FULL-FILL.** The array dies on complete rebalance; do NOT default a 50%
  consequent-encroachment mitigation. The OB/OTE 50% mean-threshold is a SEPARATE construct. Cite:
  Mentorship Ep41:100. **STATUS-QUO** (already full-fill).

## Order block · structure · timing

- **OB-9a — OB anchor = cluster-start-open (REAL change).** The order block is the **consecutive
  opposite-close cluster**, anchored at the **opening price of the candle that STARTS the run** (not the
  single last candle). Mean-threshold = midpoint of that start candle's body. Add `MaxClusterCandles`
  (default 3, raisable). Cite: Mentorship Ep3:130–133/208–221, Ep9. **DONE** (issue #53 — replaced
  `FindLastOppositeCloseCandle` with a consecutive-run finder; `OrderBlock` mean-threshold now BODY-based
  via `BodyLow`/`BodyHigh`; zone High/Low span the cluster; `OrderBlockOptions.MaxClusterCandles` default 3).
- **STRUCT-3 — Swing 3 / ITH-ITL = higher-tier fractal.** Short-term swing = 3 candles, strict inequality.
  Invalidation (ITH/ITL) = the intermediate high/low between two short-term highs/lows — a "higher-tier
  fractal proxy" (default width 5, prefer the registered ITH/ITL when available). Close-beyond invalidates,
  never wick. Cite: Mentorship Ep10/Ep12 (confirms the 5 as a proxy, not unconfirmed). **DOC/ANALYSIS**
  (relabel + prefer-actual-ITH follow-on).
- **TIME-11-12 — Multi-candle MSS (REAL change) + sweep→MSS = 5 bars. DONE (issue #59).** Displacement is now a
  price **leg**, not one candle: `DisplacementDetector` grows a backward run (consecutive same-direction,
  strictly-monotonic-extending candles, hard-capped at `DisplacementLegMaxBars`=3) and gates its **net-thrust**
  energy; the body anchor (EG-1) selects `max/min(Open,Close)` of the boundary candles leg-wide (wick on FOMC/NFP).
  `MarketStructureShiftDetector` reconstructs the leg's members from the window∩`[OriginAtUtc,AtUtc]` span and
  confirms on the **earliest** member that **closes beyond** the broken swing by `CloseBeyondMinPips` (a `FormedAtUtc`
  causality guard stops a later swing being retro-broken; a `members.Count != LegBars` fail-safe). The ONE hard rule —
  **sweep strictly precedes the MSS** — is enforced to the *breaking member* (strict `<`, within `SweepToMssMaxBars`).
  `SweepToMssMaxBars` is **5** (already the live key; the stray spec-doc '20' removed). The single-candle case is
  byte-identical (ctor overload → `OriginAtUtc=AtUtc`, `LegBars=1`); the OTE/equilibrium/SD consumers inherit the
  wider leg with zero change. Design judge-panel (3 angles → synthesis, `wf_a355c351-56d`); adversarial 4-lens verify
  (`wf_5d3750e4-e97`) SHIP. Cite: Mentorship Ep25 L320-330 / Ep5 L160-191.
  - **Provenance flag:** `DisplacementLegMaxBars`=3 is a fidelity-narrowing **cap** — Ep25's "several short-term lows"
    run is unbounded, so a long ICT-shaded run is truncated to the last 3 candles, anchoring the OTE leg / 62–79% band /
    50% PD split / SD targets *inward* of the hand-drawn run. A design choice (operator-tunable), not an ICT rule.
  - **Note:** the sweep-precede window anchors to the **breaking member**, so for an interior break the effective
    sweep→terminus reach is `SweepToMssMaxBars + (LegBars-1)`.
  - **Deferred:** a startup cross-option guard (`MarketContextOptions.WindowCapacity ≥ DisplacementLegMaxBars` and
    `≥ AtrPeriod+1`) so an under-sized window fails fast instead of silently turning every multi-candle MSS into a
    NoMatch — lands with the Host `ValidateOnStart` wiring (unreachable at defaults + same-bar-safe today).
- **TIME-10 — Reference open = instrument-class split (REAL change).** FX / daily Power-Three reference =
  **00:00 NY**; index-futures macro = **08:30 NY**. `UseMacroOpenReference=false` (default FX = midnight).
  When bearish and both exist, use the **lower** open. Cite: Mentorship Ep10/Ep2 (midnight) + Ep4/5/7/10
  (08:30 index). **CODE-READY** (add an 08:30 `MarketContext` anchor).

## Targets · grading · risk

- **TGR-1/TGR-2 — Targets = standard-deviation projections.** PRIMARY runner draw = **−1 / −1.5 / −2 SD**
  (one SD unit = the displacement leg length, **body-to-body**, reused from the SAME `ctx.DisplacementLeg`
  the OTE anchors — TGR-2 is a non-configurable single-source invariant). Negative-fib (−0.27/−0.62/−1.0) is
  **rejected** as default (Primer-flagged opt-in). Cite: §2.5.10 #5; FULL PLAYLIST SD references. **DEFERRED**
  (the §2.5.6 `StandardDeviationProjection` detector + N-tier `TargetLadder`).
- **TGR-3 — Provenance flags: status-quo.** 70.5% sweet-spot + Silver Bullet stay Primer-flagged; 3-close
  bias confirmation OFF; Scalp `AllowDirectFvgEntry=false`; `CalendarClear` (hard gate) and `CalendarDriver`
  (0.35 score) intentionally distinct; invented hold caps + M1 tie-break operator-tunable. **STATUS-QUO**
  (all already wired).
- **TGR-4 — Grading floor: auto-clear Grade B on all-required.** An all-RequiredConditions-clean setup IS
  the tradeable §2.5 model, so it grades at least **B** (A at the A threshold); the 0–100 score is the
  within-grade sorter, not a floor that demotes to C/Reject. The bare-required score (6.15/9.75 = **63**) is
  pinned by a regression test. Σ(applicable) stays 9.75 (EG-2). Consequence accepted: **Grade A is
  unreachable (~77 max) until the optional emitters (SMT, OpenPriceReference, MacroTime, CleanPriceAction, CalendarDriver)
  ship**. Cite: §2.5.2/§2.5.3/§2.5.4, spec §5 item 20. **DONE** (this slice — `SetupScorer.GradeFor`).
- **TGR-5 — Risk restore = recovery-gated.** After the loss-ladder steps down, base risk restores ONLY on a
  **≥50% recovery of the tier dip** (or a new equity high), **not on a single win** (a win cannot recover a
  multi-tier dip; §2.1/§2.5.5/§5.1 + the Primer "loser cycle" warning). The 5-win cycle is a separate
  risk-*decrease*. Win-gated restore is a deferred non-default `RestoreMode` opt-in. Cite: §2.1/§2.5.5/§5.1.
  **DONE** (PR #48 — `PaperAccount.UpdateRiskState`).

## Cross-cutting guards (X-CONFLICTS / X-IMPL-IMPACT)

- **EG-2 ✕ TGR-4 (load-bearing):** `PremiumDiscountHalf` stays one 0.85 weight ⇒ Σ(applicable) = 9.75 ⇒
  TGR-4's 63 arithmetic holds. Any future split of the PD half into a second weight **must recompute both**
  Σ(applicable) and the TGR-4 grade arithmetic. The single `ConfluenceCondition` enum + `SetupScorer` Σ is
  the one place both decisions touch.
- **Three distinct 50%s stay separate:** daily-range EQ (`PremiumDiscountOptions`, the veto) · displacement-leg
  EQ (`FvgOptions`, FVG-selection) · OB/OTE mean-threshold (`OrderBlockOptions`, an entry level). FVG-SEM-4
  forbids a 4th (consequent-encroachment).
- **Four FVG array constructs stay distinct:** wick-into void · full-fill mitigation · validity exclusions
  (flag-only) · strict-first selection.
- **One displacement-leg anchor:** EG-1 OTE entry, equilibrium, and TGR-1 SD targets all read the SAME
  `ctx.DisplacementLeg` (TGR-2). STRUCT-3's ITH/ITL is a separate higher-tier structure.

## Recommended build order (X-IMMEDIATE-ACTIONS)

1. **TGR-4** grading (DONE, this slice — unblocks the Alerting WP).
2. **OB-9a** cluster-start-open anchor (DONE — issue #53; REAL change).
3. **Additive flags** with no default-path change: EG-1 `AnchorMode`, FVG-SEM-1a `TouchSemantics`, FVG-SEM-3
   exclusions, FVG-SEM-2 `StrictFirstFvg`.
4. **TIME-11-12** multi-candle MSS, **TIME-10** 08:30 reference (REAL changes — own tested slices).
5. **DEFERRED builds:** TGR-1/2 (SD projection detector), EG-3 + FVG-SEM-2 nix (entry-orchestrator chain).
   STRUCT-3 + FVG-SEM-1b + the '20' removal are doc/test follow-ons.
