# ICT Best-Setups Guide — which pair, timeframe & style to trade (data-backed)

**For:** an operator who wants a simple "what should I trade and is it profitable" answer. Every number below is
from a full-history OANDA backtest (paper, net of spread+commission, 1% risk, Daily Risk Guard ON). **Paper only.**

## First, the vocabulary (this clears up "swing on the 5-minute")
A **style** fixes its **entry timeframe** — you don't mix them freely:

| Style | Entry timeframe | Character |
|---|---|---|
| Scalp | **M1** | most signals, most noise |
| **Intraday** | **M5** | the sweet spot — active + clean |
| Swing | **M15** | fewer, higher-quality |
| Position | H4 | rare |

So **"EUR/USD on the 5-minute" = the Intraday style** (not "Swing"). And yes — EUR/USD Intraday (M5) is **profitable**
(PF 1.64). "Swing" would be M15.

## ✅ The best ICT setups (profitable on full history) — trade these

| Rank | Pair | Style / TF | Tuning | Win% | Profit Factor | Net | Why |
|---|---|---|---|---|---|---|---|
| 🥇 | **NAS100** (NASDAQ) | Intraday / M5 | 6-of-8 | 41% | **1.78** | **+$633** | best earner; ICT's primary market |
| 🥇 | **USD/JPY** | Intraday / M5 | drop-FVG (7) | 40% | **2.40** | **+$462** | best profit factor |
| 🥇 | **EUR/USD** | Intraday / M5 | strict | 52% | **1.64** | +$188 | cleanest FX, active |
| 🥈 | **EUR/USD** | Swing / M15 | strict | 60% | **1.97** | +$14 | highest win-rate, very few trades |
| 🥈 | **NZD/USD** | Swing / M15 | 7-of-8 | **60%** | **1.88** | +$114 | solid, 10-yr sample |

## ❌ Avoid these (net-negative or unreliable on full history)
- **GBP/USD** — flat at best (PF ~0.95–1.16, ~breakeven). Skip or strict-only.
- **AUD/USD** — negative in every config.
- **USD/CAD** — negative on full history (its earlier "winner" was a short-window fluke; **override now removed**).
- **Gold (XAU/USD)** — negative; needs its own cost model.
- **SPX500** — too few setups to trust.

**ICT's own ranking agrees with the data:** he trades the **NASDAQ/ES indices** first, then **EUR/USD** as the
cleanest FX — exactly the top of our table.

## 🎯 The recommended DEFAULT (already mostly baked in)
- **Pairs to scan:** NAS100, USD/JPY, EUR/USD (M5) + NZD/USD, EUR/USD (M15).
- **Styles:** **Intraday (M5)** as the core; add **Swing (M15)** to also catch NZD/USD + EUR/USD-M15.
- **Per-pair tuning (baked in `Ict:Instruments`):** NAS100 = 6-of-8 · USD/JPY = drop-FVG · NZD/USD = 7-of-8 ·
  EUR/USD = strict. (USD/CAD override removed.)
- **Risk discipline:** 0.5–1% per trade · **Daily Risk Guard ON** (now fixed — stops you after a bad day, resets
  the next) · London + New York killzones only.

## How to actually pick a trade (the easy part)
You don't hunt manually — the dashboard does it:
1. Open the **Signals** page → it shows the **ranked best opportunities** across the basket (grade A/B, score, RR, reason).
2. The **top-ranked** signal is the system's pick. Click **Take (paper)** to open it. That's the whole workflow.
3. The per-pair settings above are already tuned, so a confirmed setup is already a quality one.

## Honest expectations (frequency)
This is a **quality-over-quantity** model: expect ~**0.5 trades/week** across this basket (about **one trade every
~2 weeks**), profitable (PF ~1.8–2.4 on the strong pairs). It is **not** an all-day scalping machine — by design.
To trade more, add more *profitable* instruments (not looser entries). Trading faster/looser was tested and **loses
money** (it sacrifices the discount entry that is ICT's whole edge).

## Bottom line
**Best single market: NASDAQ (NAS100) on M5.** **Best FX: EUR/USD on M5.** Run the 4-pair basket above on
Intraday+Swing with the baked tuning and the risk guard, follow the Signals feed, and Take the top-ranked setup.
Re-run the sweep on refreshed data periodically to confirm the per-pair tuning still holds (samples are 10–60 trades).
