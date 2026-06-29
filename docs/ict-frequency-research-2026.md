# ICT Frequency Research — grounded in ICT's OWN WORDS (2026)

**Purpose.** The cited source-of-truth for the *frequency retune*: how to surface more good, faithful ICT
setups per week across a basket — **without** abandoning ICT discipline.

## Sources (PRIMARY = ICT's own words)

The strongest source for "what ICT actually teaches" is **ICT himself** — and his official 2022 Mentorship +
Market Maker Primer videos are **in this repo as transcripts** (`2022 ICT Mentorship/*.txt`, `ICT Forex -
Market Maker Primer Course/*.txt`). Those `.txt` files are the captions of his **official YouTube videos**, so
every quote below is cited to its **episode** (e.g. `Ep17`) — primary, first-party evidence, not a third-party blog.

**Official channels (identity confirmed this session):**
- YouTube — **The Inner Circle Trader**, [youtube.com/@InnerCircleTrader](https://www.youtube.com/@InnerCircleTrader)
  (2M+ subscribers; the 2022 Mentorship is published free here — the source of the repo transcripts).
- Site — [theinnercircletrader.com](https://www.theinnercircletrader.com/) — fetched: *"I am Michael J.
  Huddleston… The Inner Circle Trader, Author of many of the trading concepts Traders are using in Forex
  today"*; it directs students to *"Study ICT YouTube."*
- X — [@I_Am_The_ICT](https://x.com/I_Am_The_ICT) (~729k followers). His verified account posts live
  **Micro E-mini S&P 500** trade executions (e.g. the "Micro Emini S&P 500 Live Trade Execution" posts) —
  first-party confirmation he trades **index futures**. (X/YouTube page *bodies* are login/JS-walled to the
  fetcher, so in-video verbatim quotes come from the repo transcripts; titles/handles corroborate.)

Third-party sites (innercircletrader.net, etc.) are **corroboration only, clearly not ICT** and not relied on.

---

## 1. Instruments — what ICT actually trades (his words)

- *"This is predominantly going to be a Futures **index** trading mentorship"* (Mentorship Ep02).
- *"it's not just limited to NASDAQ… the e-mini S&P the e-mini Dow future and the e-mini NASDAQ"* (Ep02);
  *"I'm **favoring the NASDAQ** because its volatility and its movement is more than the S&P"* (Ep07).
- *"I personally don't trade the Dow that much but there's a lot of my students that love trading the ym"* (Ep02)
  — he watches ES/Dow as references but trades NQ.
- Forex (the Primer): *"the majors that are coupled with the dollar index… **euro dollar, Cable, Aussie dollar,
  dollar yen** — there's four markets, if you just watch those it'll give you a wide array"* (Primer Ep12).
- **Avoids crosses:** *"I hate the [cross] pairs, I can't stand them"* (Primer Ep08).

**Ranked basket (his emphasis):** NASDAQ (NQ) primary → S&P (ES) co-primary → Dow (YM, reference) → FX majors
EUR/USD, GBP/USD (Cable), AUD/USD, USD/JPY. **Drop exotics/crosses.**

**Codebase:** the live basket is faithful on FX + NAS100; **US30 + gold profiles were added (Slice 1)** so the
index trio (NQ/ES/US30) is representable. Gold is NOT an ICT-core vehicle (he moved *away* from "gold silver
crude" in 1992, Ep01) — keep it secondary.

## 2. Timeframes & the top-down cascade (his words)

- Pick the cascade by trade duration (Primer Ep3): **position** = monthly/weekly/daily; **swing/short-term** =
  *"4-hour… for directional bias… 1-hour… trade management… 15-minute… for timing for entry"*; **day trade** =
  *"1-hour highest… managing on the 15-minute… 5-minute to enter."*
- The **15-minute is the "bellwether":** *"if I was held to a decision of what time frame would you be forced to
  trade with if you had to pick just one — the 15-minute… I can swing trade, short-term trade, day trade, scalp"*
  (Ep10); *"this is where it's going to give me the actual get in get out"* (Ep12).
- Intraday entry drops lower: *"this is a 5 minute chart, this is the time frame you start with and you work
  down"* (Ep04); *"the 1, 2 or 3 minute chart… the high frequency trading algorithms are operating on nothing
  really higher than 3 minutes"* (Ep02).
- **Fractal:** *"price is fractal… the same type of formation or setup can be seen on every time frame"* (Primer Ep1).

**Codebase:** the **full-matrix scanner (Slice 4)** runs each style on its canonical entry TF (Scalp→M1,
Intraday→M5, Swing→M15) and the **multi-granularity feed (Slice 2)** delivers them — faithful to the cascade.
**M5 is the primary frequency lever; M15 is the bellwether to keep.**

## 3. Sessions / killzones (his words + exact clock times)

- **FX New York killzone:** *"7:00 a.m. to 9:00 a.m. New York time… the classic ICT New York open kill zone"*
  (Primer Ep6); extended *"7 o'clock… to 10 o'clock… specific to fx"* (Ep17).
- **Index killzone:** *"index trading I'm focusing on **8:30 to 11**, and I can take a trade up to 10:40-10:45…
  not interested in taking new trades generally after 10 o'clock"* (Ep17, Ep41).
- **London:** *"2:00 a.m. to 5:00 a.m. New York time"* (Primer Ep5); *"London open generally has the **highest
  probability of creating the high or the low of the day**"* (≈70% when daily is bullish, Ep40).
- **Lunch is no-trade:** *"New York lunch hour noon to one — don't trade it"* (Mentorship).
- **Asian:** least active for him; *"can set up an OTE… 15 to 20 pips for a scalp"* (Primer Ep4) — map the range.
- **Silver Bullet** is his own published model (official video *"ICT Silver Bullet Time Based Trading Model"*),
  the 10:00–11:00 NY-AM macro.
- **Selectivity over sessions:** *"no student ever should try to trade every single session"* (Mentorship).

**Codebase:** default hunt-set `[LondonOpen, NewYorkOpen]` + index AM 08:30–11:00 are exactly his windows;
the **NY-PM Silver Bullet window is now a named opt-in (Slice 1)**.

## 4. Realistic setup frequency — the heart of "more trades" (his words)

ICT is emphatic that **frequency is the enemy**:
- *"at least **one good setup a week**"* is the baseline; *"if you can find **25 handles a week** in the e-mini
  market you can do very very well"* (Ep01). Forex beginners: *"**20 to 30 pips per week**"* (Primer Ep01).
- *"there's **three opportunities for him a day**, he's looking for just **one** of them to yield five points"*
  (Ep39) — opportunities ≠ trades taken.
- Ceiling, not a target: *"no more than **four trades — two in the morning, two in the afternoon**"* (Ep16).
- *"you **don't need to trade every single day**… if you have this impulse that you have to be trading, that's a
  **gambler's mentality**, I do not promote gamblers"* (Ep19).
- *"I have **not been touching British pound** for… months, I've not done one trade in this pair"* (Ep19) — he
  goes months idle on a pair when quality fails.

**The honest answer to "more trades per week":** a single instrument strict-§2.5 yields ≪1 setup/day. **ICT's own
prescription is NOT to loosen the setup — it's to widen the watch-list** (he watches NQ + ES + Dow + four FX
majors) and let the count come from **breadth**, taking *one good* trade when it appears. A scanner is precisely
the tool to watch that basket faithfully. **Levers, in his priority:** (1) more instruments, (2) lower entry TF
(M5→M1, more granularity), (3) more sessions/Silver-Bullet windows — never gut the sweep/MSS/displacement/FVG core.

## 5. Mandatory vs optional confluences (his explicit checklist)

ICT's **mandatory sequence** (Ep24, Ep6, Ep3) — *"this is all that is required"* (Ep3):
1. **Liquidity** — *"a pool of liquidity of buy stops resting above these highs… smart money will trade up into
   that and go short"* (Ep6).
2. **Market-structure shift** — *"once that low is broken, that's when the new trade idea is now being birthed"* (Ep6).
3. **Displacement** — *"it's got to be **energetic**, it can't be a lethargic little move… that's how you filter
   out trades that may not be high probability"* (Ep24).
4. **Fair Value Gap (entry)** — *"if there is **no fair value gap**… you **don't have a trade**, you wait or go to
   another market, because one of them is going to be there **every single trading day**"* (Ep6).
5. **Premium/Discount** — *"above equilibrium is premium, below 50% is discount"* (Ep10) — confirms bias.

**Explicitly OPTIONAL:**
- **Order blocks:** *"I'll look for order blocks but I'm going to try to **stay away** from order blocks… because I
  have **models that don't even rely on order blocks**"* (Ep05).
- **OTE fibs (62–79%):** a precision refinement, not a gate (Primer OTE; Ep24 *"you don't need to know that"*).

**This is exactly the repo's k-of-n design:** the sweep/MSS/displacement/FVG/bias core is mandatory; OB and OTE
precision are the relaxable levers — and the retune only relaxes them per-pair where the backtest proves net gain.

## 6. Higher-frequency, still-faithful variants

ICT's own additive, higher-recall models the repo could add (all his, per his channel): **Turtle Soup**
(false-break reversal — higher recall than the strict 2022 AND-gate), the **Silver Bullet** time-based model (his
own, a recurring daily window), and **SMT divergence** on NQ/ES (a *quality* filter, not a frequency add). The
**2024 model** is a more granular (M1) sibling of §2.5. The repo lacks Turtle Soup + the NY-PM Silver Bullet
window (Slice 1 named it) — those are the cleanest faithful frequency adds.

---

## 7. Retune results (2026-06) — backtests on the now-faithful matrix

In-memory optimizer over the fetched OANDA history (`data/*.csv`), Intraday, 1% risk, **picked by NET P&L after
costs, ≥~15-trade rule** (the repo's hard convention — small samples are flukes). Data: M15 = robust 8-yr
(2018→2026); M5 = ~2.7-yr.

| Instrument | Best | trades | win% | avgR | PF | maxDD | NET | Verdict |
|---|---|---|---|---|---|---|---|---|
| **EUR/USD** | M15 strict 8 | 15 | 60% | +0.28 | **1.97** | 1.33R | +$14 | best FX *quality* — STRICT |
| EUR/USD | M5 strict 8 | 22 | 50% | +0.13 | 1.40 | 3.40R | +$160 | strict also net-positive |
| **NAS100** | M5 k=6 | 22 | 41% | +0.36 | 1.78 | 4.0R | **+$633** | best NET (strict-8 = −$101) |
| **USD/JPY** | M5 drop-FVG | 10 | 40% | +0.49 | **2.40** | 2.5R | +$462 | best PF (strict-8 = 0 trades) |
| GBP/USD | M15 strict | 21 | 38% | −0.02 | 0.95 | 5.3R | +$67 | marginal — STRICT |
| AUD/USD | M15 strict | 15 | 40% | −0.07 | 0.79 | 2.8R | −$185 | weak — STRICT, no bake |
| **XAU/USD (gold)** | M15 strict | 11 | 45% | +0.11 | 1.26 | 2.7R | −$80 | first valid run (sizes now) — net-neg, NOT baked |
| SPX500 | M5/M15 | 2–9 | — | — | flukey | — | net-neg | sparse (<15) — no bake |

**No new bakes warranted** — every net-positive ≥15-trade winner is already the live default (NAS100 M5 k=6;
USD/JPY M5 drop-FVG; EUR/USD/GBP/USD/AUD strict). **Gold now *trades* (the Slice-1 metal sizing fix)** but is
net-negative in every config → flagged for a future draw/cost model, not baked.

**Aggregate setups/week:** the net-profitable basket (EUR/USD M15 + NAS100 M5 + USD/JPY M5) ≈ **2.83/week**;
a discovery-mode recall ceiling (M5 + k=5 across all 7) ≈ **10.4/week** — but the FX legs net-lose at k=5, so
that's a recall ceiling, **not** a profitable stream. This is ICT's own thesis in data: **breadth + lower TF
raises the count; gutting confluence loses money.** Re-run the optimizer when more M5 history (or US30 data) is fetched.
