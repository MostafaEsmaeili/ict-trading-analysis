# ICT 2022 Mentorship — External Web / Community Research (June 2026)

> **Scope & method.** This is a *web/YouTube/community* research pass — deliberately OUTSIDE the repo's own course
> transcripts (which remain the primary source for the §2.5 model). Compiled from a 5-angle parallel WebSearch fan-out
> (≈55 distinct queries). WebFetch was egress-rate-limited, so findings are sourced from WebSearch result snippets,
> which were detailed enough to cite. Attribution: **[H]** = sourced to Huddleston/ICT; **[C]** = community-secondary.
> Treat all quantified win-rates as **self-reported / unverified** unless noted — see §6.

---

## 1. The mechanical entry model (community consensus)

The community codifies the 2022 model as an algorithmic intraday model on three pillars: **HTF daily bias → session
liquidity sweep → LTF entry off a refined PD array.** The canonical step order (Forex Factory [TFlab] thread,
innercircletrader.net, TradingFinder, TradeZella):

1. **Mark the range** — high/low of the **00:00–08:00 NY** dealing range (Asian range 20:00–00:00 EST for the sweep).
2. **Liquidity sweep / Judas** — after the open, wait for a sweep of range high/low; highest-odds Judas hits **02:00–05:00 EST**.
3. **MSS on a lower timeframe** (M5/M3/M1) in the direction of the daily bias.
4. **Displacement** — the breaking candle must close decisively and leave an imbalance (FVG).
5. **PD array** — the FVG and/or order block left by the displacement leg.
6. **OTE retrace entry** — the **62–79%** fib zone; **0.705 "sweet spot"** (the algorithmic midpoint of 0.618/0.79,
   *not* a true fib — matches the repo's EG-1 "70.5% is Primer-flagged only" note).
7. **Target the draw on liquidity** — TP at the opposite-side pool (old highs for longs, old lows for shorts), **≥ 1:3 RR** (some accept 1:2).

**Refinements the community treats as interchangeable:** Consequent Encroachment (exact **50% of the FVG** — "the most
reactive level inside the gap"), OTE 0.705, or the FVG edge (IOFED). **Reference opens:** midnight open (00:00 NY) is the
primary daily-bias pivot; 08:30 (news) and 09:30 (NYSE) are key intraday levels.

> **✅ Alignment with this repo's §2.5 model:** the community sequence (bias → sweep/Judas → MSS+displacement →
> FVG/OB+OTE 62–79% → draw on liquidity) matches the repo's mined model **nearly exactly**, including the 0.705 sweet
> spot, 50% CE, midnight/08:30/09:30 opens, and the ≥1:3 RR floor. The biggest community *debate* is whether an MSS
> requires displacement (strict camp: yes) vs. treats it as a quality filter — the repo takes the strict line.

## 2. Best instruments · sessions · timeframes

- **Instruments (ICT's own preference):** **NASDAQ-100 (NQ) and E-mini S&P 500 (ES) index futures** — "the time-of-day
  delivery is cleanest on the US index futures"; the Silver Bullet/Macro strategies "were initially developed and tested
  on NQ and ES." FX majors are secondary (**EUR/USD** cleanest/best to learn, then GBP/USD, AUD/USD, USD/JPY); **gold
  (XAU/USD)** works but is event-driven with deep 10–20pt sweeps (vs ~5 for FX). **Avoid exotics/crosses.**
- **Sessions (NY local):**
  - **London Open 02:00–05:00** — best for **EUR/USD & GBP/USD**; "sets daily direction ~70–80% of days," forms the day's high/low via the Judas sweep.
  - **NY AM 07:00–10:00 (08:30–11:00 indices)** — highest volume (London–NY overlap + 08:30 data + 09:30 open); usually the *second leg* of London's move.
  - **Silver Bullet 10:00–11:00** — best on **NQ/ES** ("highest returns when this strategy is applied").
  - **London Close 10:00–12:00** — lower-vol reversal/scalp window.
- **Timeframes (top-down):** Intraday = Daily bias → H4 zone → M15 trigger → M5 precision; Scalp = H1 → M15 → M5/M1;
  Swing = Weekly → Daily → H4/H1. **"Without a clear HTF bias, no entry is taken."**

## 3. Realistic setup frequency

ICT is **low-frequency**: "designed for **1–2 high-quality trades per day**, not constant action"; "If London didn't
produce a setup, wait for NY AM. If neither did, there's no trade today — and that's fine." The Bread-and-Butter model
yields "**2–3 quality setups daily, one per session**." Most consistent traders trade **one session, one setup, 1–2
trades/day max.**

> **✅ Validates the repo finding:** your high-precision/low-recall M15-strict engine (~2 setups/yr/instrument) matches
> the methodology's *intent* (quality over quantity); the M5/M1 + relaxed-k-of-n "discovery mode" maps to the
> discretionary-scalper end of the spectrum. ~1 quality setup/day/instrument is the ICT norm to target.

## 4. Risk & trade management

- **Risk/trade:** **1–2% max, prefer less** (0.5% / 0.25% for scalps; a documented ICT example uses ¼% on a 30-pip stop).
- **Loss ladder (anti-martingale):** cut **1% → 0.5% → 0.25%** in drawdown; **restore only after a recovery / new high**,
  not a single win (matches the repo's recovery-gated restore). 50% loss needs 100% gain → keep risk small.
- **Circuit breakers (community/prop, NOT ICT-verbatim):** **stop after 2 consecutive losses** (some use 3) "the
  post-streak trade is almost always the worst of the day"; **daily loss cap ≈ 2–3%** (prop firms 5%); **weekly "stop by
  Wednesday"** once the week's move/target is in.
- **RR:** floor **2:1**, standard **3:1+**, runners 5–10R; discipline = "take your 2R and close the laptop."
- **Management:** **partial at T1** → move stop to **breakeven** (or BE at +1R / +2R); **no overnight** — close intraday
  by session end, use **time-based stops**. *Caveat:* BE-too-early / over-partialing "bleeds off exactly the profits you
  need" if your edge is 3R+ runners — know your R-distribution.

> **🛠️ Implemented this session:** the **Daily Risk Guard** (stop-after-N-losses + daily-loss-cap halt) — the missing
> enforcement half of this discipline (the engine had the loss-*ladder* but never *halted*). Default-off, opt-in,
> provenance-flagged numbers (N=3 from the 3-rung ladder; 2% daily cap), since these are community/prop canon, not
> 2022-Mentorship-verbatim.

## 5. A+ confluence filters (ranked by community)

1. **HTF daily-bias alignment — the single biggest claimed win-rate lift.** "The 5-minute must agree with the daily; the
   daily need not agree with the 5-minute." Canonical A+ stack: a 5m FVG inside a 4H order block inside a daily discount
   zone, during a killzone.
2. **SMT divergence** (two correlated assets diverging at a swing) — **a confirmation, never a trigger**; cleanest on
   **NQ vs ES** (FX: EURUSD vs GBPUSD / USDX). An SMT *against* HTF bias is more likely to fail — filter through bias.
3. **LRLR over HRLR draws** — target **Low-Resistance Liquidity Runs** (clean displacement, leaves FVGs); **avoid
   High-Resistance** runs (choppy, defended, need a news catalyst). Cuts time-stops + drawdown.
4. **Order block vs breaker vs mitigation** — the **body-close past the OB extreme** is the single diagnostic: held →
   mitigation (continue same direction); failed/closed-through + swept + MSS → breaker (trade opposite).
5. **Silver Bullet (10–11 NY)** — time-boxed FVG entry; "highest-probability" by session structure, **no rigorous win-rate published.**
6. **Power of 3 / AMD** — Accumulation (Asia) → Manipulation (London Judas) → Distribution (NY true move).
7. **News avoidance (FOMC/NFP/CPI)** — **do not trade pre-release**; stand aside, let the release sweep one side, trade
   the post-news displacement toward the HTF draw.

## 6. Does it actually work? — balanced evidence

- **Quantified backtests are mostly small/promotional:** vendor/indicator sources cite **60–70% win, ~1:1.8 managed RR,
  8–15% max DD**; Silver Bullet runs of **62.5% (10 days)** / **81% (21 trades)** — but **tiny samples, regime-dependent**
  (great July 2023 NAS100, hard to replicate). TradeZella's own credibility bar (**PF > 1.3, 200+ trades, positive
  expectancy**) is one **most public ICT backtests do not meet**.
- **Prop-firm reality:** estimated **~20–25% challenge pass**, **~3–5% challenge+verification first-try**; **~70% of
  failures are loss-limit breaches.** Traders risking **≤1% pass at 35–67%** vs **12–15% at 2–3%** — **risk control, not
  the entry pattern, dominates outcomes.**
- **Skeptical strand (must be heard):** **no independently verified ICT track record**; documented poor public-competition
  results (2016 $1M attempt; 2024 Robbins Cup −60% DD, never on the leaderboard); critics call the framework a Wyckoff/
  supply-demand repackage with **curve-fit/unfalsifiable** risk (so many PD arrays you can explain any move post-hoc).
- **What distinguishes profitable ICT traders:** discipline, not a magic entry — **master ONE setup, journal, start
  small, restrict to killzones, align with HTF daily bias, ≤1% risk.** "Why 90% fail" is attributed to mindset/overtrading,
  not the strategy.
- **Sustainable expectancy reported:** **~50–65% win at 1:2–1:3 RR, ≤1% risk, validated over 200+ trades.** Discount the
  70–90% marketing win-rates (small samples / favorable regimes).

> **⚖️ Honest bottom line for this project:** ICT's *components* (sessions, liquidity, HTF bias, ≤1% risk, RR-driven
> targeting) are credible and align with what makes prop traders pass — but the reported edge comes from **selectivity +
> risk discipline**, not the entry pattern alone. This is exactly why this engine is **paper-only + defensive**, why the
> strict §2.5 model is high-precision/low-recall by design, and why the highest-leverage improvements are **filters**
> (HTF bias, LRLR draws, news/SMT) and **risk discipline** (the new Daily Risk Guard), not more entry triggers.

## 7. Highest-leverage additions this research points to (for the engine)

| Rank | Addition | Why (web consensus) | Status in engine |
|---|---|---|---|
| 1 | **HTF daily-bias filter** | "single biggest win-rate lift" | intraday `BiasAligned` exists; a true Daily-TF filter is the top open lift |
| 2 | **Daily Risk Guard** (stop-after-N + daily cap) | risk control dominates prop outcomes | **✅ built this session (opt-in)** |
| 3 | **LRLR-over-HRLR draw selection** | higher-prob targets, less drawdown | partial (draw targets untapped pools); HRLR scoring is a follow-on |
| 4 | **SMT divergence (NQ/ES, EUR/GBP)** | top A+ confirmation | enum slot exists; needs cross-symbol context (heavier) |
| 5 | **Silver Bullet 10–11 killzone** | best index/scalp sub-window | **✅ added as a selectable killzone this session** |

---

*Sources (representative; ~70 URLs across the 5 angles): innercircletrader.net (model, killzones, OTE, daily bias, SMT,
Silver Bullet, HRLR/LRLR, Power-of-3), forexfactory.com [TFlab] 2022-mentorship thread, tradingfinder.com, ictkillzone.com,
tradezella.com, fxnx.com, phidiaspropfirm.com (ICT guide, "is ICT legit", order blocks), edgeful.com (ES vs NQ),
altrady.com ("why 90% of ICT traders fail"), forexpeacearmy.com + powertrading.group + tradingrage.com (critiques),
quantvps.com + traderssecondbrain.com (prop pass rates), ttrades.com, ftmo academy. Full per-claim URLs are in the
session research transcripts.*
