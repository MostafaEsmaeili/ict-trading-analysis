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
- **Instrument-class split** (§2.5.7): FX uses NY 07:00–10:00 / London 02:00–05:00 / Close 10:00–11:00 /
  PM 13:30–16:00; index futures use AM 08:30–11:00. HARD no-trade lunch 12:00–13:00.

## Primitives
- Swing high/low = 2 lower-highs / higher-lows either side (3-candle definition).
- FVG (3-candle): bullish `c1.High < c3.Low`; bearish `c1.Low > c3.High`. Two-touch invalidation: a 3rd
  return into the gap voids it.
- Order block: last down-close before an up-move = bullish OB (key = its open, +1 tick / +3 pips FX;
  mean-threshold = 50% of OB body); mirror for bearish. A valid OB requires its associated FVG.
- Liquidity: buy-side above old/equal highs, sell-side below old/equal lows. Sweep = wick beyond a
  level then close back inside (vs a "run" = continuation through it).
- Displacement = energetic move creating an FVG; precondition for a valid MSS/entry (quantify the
  energy with a body/ATR filter — §2.5.7).
- MSS/CHoCH = break of a short-term swing AFTER a liquidity sweep.

## Entry / bias / risk
- OTE = fib retrace of the impulse leg, anchored body-to-body (wicks only FOMC/NFP): entry 0.62–0.79,
  sweet spot **0.705** (Primer-sourced default — confirm provenance), targets at 0 and negative
  extensions; also −1/−1.5/−2 SD projections. Equilibrium = 50%.
- Daily bias = discount(<50%)/premium(>50%) of the daily dealing range + draw-on-liquidity; neutral = no trade.
- Risk default **1%** with-bias (developing 0.25–0.5%, max 4.5%); loss-ladder 1%→0.5%→0.25%, restore
  after recovering 50% of the loss. Min RR ~2.5R (configurable default, not a hard transcript rule).

## THE mechanical entry model (mined from the 2022 Mentorship — plan §2.5)
**ICT 2022 Intraday FVG Model — Liquidity Sweep → MSS/Displacement → PD-Array OTE Entry.**
Confirm a setup ONLY when ALL of these hold (RequiredConditions):
1. **Bias** — daily one-sided: discount (<50% of daily range) = bullish, premium (>50%) = bearish; neutral = NO trade. Trade only with bias.
2. **Draw on liquidity** — a valid opposing target exists (relative-equal H/L, prior-day H/L, HTF FVG, big figures 00/20/50/80).
3. **Killzone** — inside an enabled window (FX: London Open 02:00–05:00, NY 07:00–10:00, London Close 10:00–11:00, PM 13:30–16:00; Index: AM 08:30–11:00). NEVER lunch 12:00–13:00.
4. **Liquidity sweep** — price raids a prior high/low AGAINST the trade direction (Judas vs midnight/08:30 open).
5. **MSS + displacement** — an energetic candle breaks a prior 3-candle swing and CLOSES beyond it (weak/wick = NO trade).
6. **FVG** — first 3-candle gap found 15m→1m within the displacement leg (none → NO trade), in the correct half: shorts ≥50%, longs ≤50%.
7. **Entry** — limit in the FVG/OB at OTE **62–79%** (sweet-spot 70.5%); OB entry = OB open +1 tick / +3 pips FX.
8. **Stop** — beyond swept swing / FVG / OB (1–2 ticks, ~10 pips FX); clear the farther of two stacked FVGs.
9. **Targets** — T1 nearest FVG/50%/short-term level (partial), T2 opposing liquidity / HTF draw; min RR ~2.5R (configurable). Trail 50%→25% risk, 75%→breakeven. Max hold 90–120 min, no overnight.

**Grade/alert gate:** score = Σmatched weights / Σapplicable ×100. A ≥80, B 65–79 (both need all RequiredConditions), C 50–64 (watchlist), Reject <50. **Alert floor 65.** Weights & full detail in plan §2.5.

**Caveats (verify medium-confidence, §2.5.7):** 70.5% sweet spot & Silver Bullet are Primer/canon, not grounded in these 2022 eps — keep configurable; FX vs index killzone windows are SEPARATE (instrument-class switch, default FX); ~2.5R is a default not a hard rule; quantify "displacement" with a body/ATR filter; detectors must emit FVG two-touch + ITH/ITL **invalidation**, not just formation.

## Web cross-check resolutions (plan §2.5.10 — transcripts stay PRIMARY)
On any web-vs-transcript conflict, the transcripts win. Keep these PRIMARY defaults: OTE fib **body-anchored**
(wicks only FOMC/NFP); min RR ~2.5R configurable (not 3); London Close **10:00–11:00**; lunch **12:00–13:00**;
targets = **−1/−1.5/−2 SD** (not negative-fib); Asian **19:00–00:00**; FX NY **07:00–10:00**. Adopt as
configurable additions: FVG validity exclusions (no-sweep / Asian / counter-bias / no-CHoCH / overlapping
wicks → reject), FVG mitigation + two-touch invalidation, ATR displacement filter, broken-swing-only fib
anchoring + array inversion on BOS, break-even-at-1R, portfolio risk cap ≈5%. Provenance-flag (configurable,
NOT Mentorship-verbatim): 70.5% sweet spot, Silver Bullet windows, PD-array 7-tier ranking, 25–75% quadrant.
Never hard-code community win-rate stats.

## Trade style & timeframe (plan §4.7)
A `TradeStyle` (Scalp/Intraday/Swing/Position) picks the timeframe triple (Bias/Structure/Entry) from the
ICT top-down cascade Daily→H1/H4→M15→M5/M3→M1. Default = **Intraday** (the §2.5 model). Config: `Ict:TradeStyles`
+ `Ict:Scanning:ActiveStyles`. Every Setup is tagged with its style + trigger timeframe.

## Time zone (plan §4.8)
ICT times are **New York** with US DST. UTC is the only stored truth; NY math goes ONLY through `NyClock`
(`America/New_York` via ICU — never the Windows `"Eastern Standard Time"`); never use the ambient host zone.

## Guardrail
Analysis + paper trading ONLY. Never propose or implement a live-order path.
