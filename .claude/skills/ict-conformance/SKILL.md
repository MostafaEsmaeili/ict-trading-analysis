---
name: ict-conformance
description: Gate EVERY code change against the ICT model. Use after writing/modifying any detector, confluence rule, killzone/session logic, bias, trade-style/timeframe, paper-trade sizing/targets, or execution-cost code — verify it still matches the transcript-mined entry model (plan §2.5) and the web cross-check resolutions (§2.5.10), with exact parameters from Options/Resources (no magic numbers/strings). Run on each change before calling it done.
allowed-tools: Read Grep Glob Bash(git diff *)
---
# ICT conformance check (run on every change)
!`git diff --stat`

For each changed file touching trading logic, verify against `ict-methodology` + plan §2.5/§2.5.10:
1. **Rule fidelity** — does the change encode the ICT rule exactly? Cite the §2.5 step / episode. No invented parameters.
2. **Contested-point defaults respected** (§2.5.10): OTE fib **body-anchored** by default (wicks only FOMC/NFP);
   min RR configurable ~2.5R (not 3); London Close 10:00–11:00; lunch 12:00–13:00; SD targets −1/−1.5/−2;
   Asian 19:00–00:00; FX NY 07:00–10:00.
3. **Provenance flags** — 70.5% sweet spot, Silver Bullet, PD 7-tier ranking, quadrants = Primer/secondary
   defaults, configurable, NOT presented as Mentorship-verbatim. No hard-coded win-rate stats.
4. **Time zone** — NY math only via `NyClock`/`America/New_York`; never the ambient host zone (§4.8).
5. **No magic numbers** → Options POCO (`Ict:*`); **no magic strings** → `.resx`.
6. **Determinism + DDD** — logic in the domain (pure), not a handler; detector emits invalidation, not just formation.
7. **Guardrail** — nothing introduces a live-order path.
Output: per-file PASS / NEEDS-FIX with the specific §2.5 reference. For non-trivial domain changes, escalate
to the `ict-domain-expert` agent. NEEDS-FIX blocks "done".
