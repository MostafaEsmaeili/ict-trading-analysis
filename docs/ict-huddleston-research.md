# ICT / Michael Huddleston — Research Report (operator reference for tuning the engine)

> Compiled 2026-06-28 from (a) the **primary transcripts in this repo** (41-ep 2022 Mentorship + 24-ep
> Market Maker Primer — Michael Huddleston's own words) and (b) a **WebSearch fan-out** over community
> summaries (innercircletrader.net, opofinance, LiteFinance, TradingFinder, Altrady, Phidias, etc.).
> WebFetch was platform-rate-limited during this run, so the web layer is snippet-sourced; the
> transcript layer is authoritative. Attribution: **[H]** = Huddleston-direct (transcript or sourced to
> him), **[C]** = community-secondary.

## 1. Course & how to learn it
- **[H]** The canonical free curriculum is the **2022 ICT Mentorship** (41 episodes), studied in order
  1→41; the **2016 Private Mentorship Core Content** is the foundational primer beginners are pointed to.
- **[C]** Concept order: PD-array, order block, FVG, killzones → liquidity → market structure
  (BOS/CHoCH/MSS) → Power-of-3 (AMD) → the full 2022 model.
- **[H]** The 2022 entry model is the fixed 4-phase sequence this engine already encodes (plan §2.5):
  **liquidity sweep (Judas) → MSS + displacement → retrace into FVG/OTE (62–79%, 70.5% sweet spot) →
  target opposing liquidity.**
- **[C]** Path to consistency: master one concept at a time; trade only in killzones; backtest the ONE
  model over **≥2 years / 100+ trades** targeting **profit factor > 1.5** + positive expectancy, then
  forward-test on demo before scaling. (Our optimizer + backtester operationalise exactly this.)

## 2. Recommended instruments / pairs (ranked)
- **Tier 1 — [H] the model's home turf: NASDAQ-100 (NQ) and E-mini S&P 500 (ES) index futures.** The
  2022 Mentorship is *literally a NASDAQ e-mini mentorship* ("the main focus of this mentorship");
  he also trades ES, rarely the Dow. Cleanest time-of-day delivery; NQ/ES are the cleanest **SMT** pair.
- **Tier 2 — FX majors:** **EUR/USD** (his primary teaching pair; "cleanest, most textbook setups" — the
  best to LEARN on and our strongest backtest combo), **GBP/USD** (Cable), then **AUD/USD** and
  **USD/JPY** — the four USD majors he names ("euro dollar, Cable, Aussie dollar, dollar yen").
- **Optional — XAU/USD (gold):** the model "is applicable to gold," but gold is "an event-driven market";
  he moved *away* from gold/silver/crude to index futures. Use only with tightened risk.
- **Avoid — [H/C] exotics and crosses** (pound-swiss etc.): "that's an exotic, I don't trade them."
- **Beginners:** master **EUR/USD** first (3–6+ months, one pair). Mind correlation (EUR/USD≈GBP/USD).

## 3. Money management & risk
- **[H] Risk per trade: 1% MAXIMUM — "preferably half of one percent or maybe a quarter of one percent."**
  (Our `Ict:Risk` default 1% base + loss-ladder matches.)
- **[H/C] Loss-ladder / no revenge sizing:** step risk DOWN after losses (1% → 0.5% → 0.25%); **stop
  after 2 consecutive losses**; hard **daily loss cap 2–3%**; never increase size after a loss.
- **[H] Reward-to-risk:** floor **2:1**, standard **3:1**, OTE typically 2:1–4:1, runners to 8:1–10:1 on
  high-conviction days. *Nuance [H]:* "a risk-to-reward model is [not] essential to be net profitable" —
  a high win-rate can carry a lower RR; don't force RR at the cost of win-rate.
- **[C] Drawdown:** 1% sizing ⇒ 10 losses ≈ 10% DD (manageable); the asymmetry-of-recovery (a 50% loss
  needs a 100% gain) is the reason to keep risk small. **Lower drawdown comes from fewer/cleaner trades
  (killzone + bias + news gating), not from a magic stop.**
- **[H] Position sizing:** scale to setup quality within the cap; pyramid into winners with **decreasing**
  add sizes.

## 4. Techniques to improve win-rate / RR / drawdown / profit
- **[H] Restrict entries to killzones.** London Open 02:00–05:00 NY (most often forms the day's high/low);
  NY AM 07:00–10:00 (08:30–11:00 indices). EUR/USD & GBP/USD in London; USD majors + indices in NY AM.
- **[H] Silver Bullet:** a 10:00–11:00 NY sub-window (liquidity grab → displacement → FVG retrace) — a
  strong index/scalp sub-config.
- **[H] HTF daily-bias alignment is the single largest win-rate filter** — never take a 5m entry against
  the Daily/4H bias; sit out balanced/inside-day ranges.
- **[H] Target the draw on liquidity** — set targets AT the opposite pool (sweep one side → draw to the
  other) to maximise RR; **prefer Low-Resistance Liquidity Runs, never target High-Resistance (HRLR)**
  runs (slow/defended/news-driven). Avoiding HRLR cuts time-stops + drawdown.
- **[H] OTE 62–79% only when it coincides with a PD-array element; stack confluence** (FVG + OB nested in
  the OTE band) for A+ entries; **stop just beyond the swept extreme**, never "improve" it.
- **[H] Don't trade high-impact news (FOMC/NFP/CPI)** — "trading these events is gambling"; stand aside
  ~30 min around the release until the sweep happens. (Our `CalendarClear` gate — now feedable, slice #168.)

## 5. Common pitfalls + fixes
- **Overtrading** → let killzone gating force fewer, higher-probability trades.
- **No / mis-read daily bias** (reading bias off 1H/15m) → read it on the Daily; sit out inside-day ranges.
- **Trading outside killzones / pre-positioning before the window** → restrict to the windows; wait.
- **Chasing instead of waiting for the OTE retrace** → take entries at the discount/premium OTE level.
- **Entering the killzone's first leg (the Judas sweep)** → let the sweep happen, trade the displacement.
- **Over-risking "perfect" setups / moving the structural stop** → fixed small risk; stop stays beyond
  swept liquidity.
- **Going live too early / no journaling / mindset** → backtest, journal, 6–12 months demo (>90% fail on
  mindset + impatience, not on the model).

## Actionable for THIS engine's optimizer
1. **Prioritise instruments:** EUR/USD (strict §2.5 — already our best) + NAS100 first; add the missing
   Tier-1 **S&P/ES (SPX500)**; then GBP/USD, USD/JPY, AUD/USD; XAU/USD only with tightened risk; **never
   exotics/crosses.**
2. **Sessions:** keep `ActiveKillzones = [LondonOpen, NewYorkOpen]`; index killzone anchored to the 08:30
   macro open (already wired). A 10–11 NY Silver-Bullet window is a future index sub-config.
3. **Risk/RR:** base 1%, ladder 1→0.5→0.25, stop-after-2-losses + 2–3% daily cap (engine has the ladder;
   a daily-loss cap is a candidate add); RR floor 2:1 / target 3:1 with a T1 partial + breakeven.
4. **Confluence — require** the full chain (sweep → MSS → bias → FVG/OTE → draw → killzone → calendar);
   **relax per-asset only, data-driven, opt-in, flagged** (NAS100 trades better without requiring FVG;
   keep EUR/USD strict). **Never drop `DisplacementMss`** (direction lock). Strict §2.5 stays the global
   default.
5. **Drawdown:** the data shows EUR/USD ≈1.3R max-DD (clean), NAS100 ≈3R, **GBP/USD 5–8R (weak/high-DD —
   its prior aggressive 13-trade bake is likely overfit and should be reconsidered/reverted).** Favour the
   low-DD instruments + the news/HRLR gating above to hold drawdown down.

*Sources (web layer, snippet-sourced): innercircletrader.net tutorials (killzones, daily bias, OTE,
Silver Bullet, HRLR/LRLR, SMT), opofinance "best pairs to trade with ICT", LiteFinance ICT guide,
TradingFinder 2022 model, Altrady "why 90% of ICT traders fail", Phidias prop-firm ICT guide,
SmartMoneyICT risk management, thesimpleict backtesting. Primary layer: the repo transcripts (2022
Mentorship FULL PLAYLIST; Market Maker Primer FULL PLAYLIST), already mined into docs/PLAN.md §2.5.*
