---
name: pessimistic-judge
description: A ruthless, guilty-until-proven-innocent adversarial review harness for THIS repo. Use to hammer any implementation (a slice, the entry/fill geometry, a backtest result, a config bake) before trusting it — it hunts look-ahead/phantom fills, guardrail leaks, ICT-fidelity drift, numeric/money bugs, overfit/unrealistic backtest results, determinism breaks, magic numbers/strings, config-binder traps, and test gaps, returning a prioritized, evidence-backed findings list with a SHIP/BLOCK verdict. Run it in the overnight improvement loop and before any merge.
---

# Pessimistic Judge — assume it's broken until the evidence says otherwise

**Philosophy.** Default verdict is **GUILTY**. A finding is only dismissed with concrete `file:line`
evidence that the failure mode CANNOT occur. Implausibly good results are treated as **bugs, not edge**
(a backtest profit factor > ~3 or a win-rate > ~70% over a non-trivial sample is presumed a look-ahead /
phantom-fill / accounting error until proven otherwise — see Dimension 1). Be specific, cite lines, rank by
severity (Critical / High / Medium / Low), and propose the fix.

## The attack dimensions (each a skeptical verifier)

1. **Look-ahead / phantom fills / optimism (highest priority).**
   - Does any path open/fill at a price the market had not yet traded to ON or BEFORE that bar? (The
     canonical bug: `EntryMode.Immediate` opening at `plan.Entry`/OTE before price retraced there.)
   - Do limit fills require a LATER bar's High/Low to actually trade through the level (and cancel/expire
     if price never returns)? Is the confirmation-bar excluded from managing its own setup (§4.1 no-look-ahead)?
   - Is a result "too good"? PF, win-rate, avg-R, equity slope — sanity-bound them; a PF of 30+ is a bug.
2. **Defensive guardrail (non-negotiable).** No broker/order/executor symbol; feeds read-only by shape;
   `LiveTradingEnabled` validates false; the Take/open path is the SINGLE `SetupTradeOpener` simulated path.
3. **ICT fidelity (§2.5 / docs/ict-core-model-decisions.md / the transcripts).** No new `ConfluenceCondition`
   weight (Σ=9.75 must hold); killzone/OTE/displacement/bias math unchanged unless flagged; every invented
   number provenance-flagged (INVENTED/CONVENTION/DERIVED).
4. **Numeric / money correctness.** Frozen-1R (§5.2), net vs gross P&L, cost double-counting (spread booked
   once), lot-step flooring, decimal precision, idempotency on the deterministic setup id, the ~5% cap.
5. **Determinism.** Same input → byte-identical output; no `DateTime.Now`/ambient clock; ranked feeds total-order.
6. **Overfit / sample honesty.** Bakes only on full-history ≥~15-trade samples picked by NET P&L (not gross PF);
   2-yr/tiny-sample wins flagged as flukes; a per-pair tuning must beat strict on the ROBUST window.
7. **Config-binder traps.** Operator-settable collections default EMPTY + resolve in code (the .NET binder
   APPENDS to non-empty initializers); a new live knob rides the revision-stamped RuntimeSettings + cache-eviction.
8. **Test gaps.** Is the failure mode actually tested? Docker integration + E2E RUN (they catch DB/SQL bugs unit
   tests miss). New behavior has a regression test; default-off changes proven byte-identical.
9. **Magic numbers/strings.** Every constant in Options/resx; no inline literals in trading logic.

## How to run it

Prefer ONE focused pass per target (rate-limit-resilient — avoid 20-agent fan-outs that get throttled):

1. **Gather the diff/target** (`git diff main --stat`, read the changed trading/backtest files).
2. **Run the skeptical verifiers** — either inline (read + reason per dimension) or as a small `Workflow`
   panel (≤6 agents, one per high-priority dimension), each instructed: *"assume this is broken; find the
   concrete failure or prove with file:line it can't happen; default to GUILTY."* For backtest results,
   ALWAYS re-derive a suspicious number by hand (e.g. recompute PF from the trade list).
3. **Adversarially verify each finding** (don't trust the first read): try to REFUTE it; keep only the ones
   that survive. Empirically confirm where possible (run the backtest, count the funnel, re-derive the metric).
4. **Verdict**: a ranked table (Critical→Low) with `file:line` + evidence + the fix, and SHIP / BLOCK.
   BLOCK on any Critical (look-ahead, guardrail, money) — never wave it through.

## In the overnight improvement loop

Each round: backtest the current state → run THIS judge over the changes + the backtest realism → fix the
top confirmed finding (gated: build 0-warnings + tests + guardrail 7/7 + ict-conformance) → `/update-memory`
→ repeat. The judge's surviving backlog is the next round's work-list. Stop a line of work when the judge
goes "dry" (no Critical/High for 2 rounds) and move to the next weakest area.
