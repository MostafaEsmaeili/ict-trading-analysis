---
name: verify-ict-system
description: End-to-end manual verification of the running ICT system — start Postgres + API host + dashboard, replay a known candle fixture, and confirm an alert fires with correct ICT reasoning, a paper trade opens and closes with realistic costs, the OHLC chart shows the pattern overlays, and performance metrics update. Use to validate a change in the real app (not just unit tests).
allowed-tools: Read Grep Glob Bash(docker *) Bash(dotnet *) Bash(npm *)
---
# Verify the ICT system end-to-end
1. `docker compose up -d postgres` (or let Testcontainers handle test runs).
2. `dotnet run --project src/IctTrader.Host` — confirm the boot log prints `DEFENSIVE MODE: analysis +
   paper only`, that startup validation passes (LiveTradingEnabled=false), and that the timezone validator
   resolved `America/New_York` (plan §4.8).
3. Point the feed at the **Replay** provider loaded with the `BullishLondonKillzone` fixture (or OANDA
   practice for a live smoke test).
4. `cd web/ict-dashboard && npm run dev`; open the dashboard.
5. Confirm:
   - an alert appears with a reasoning string like "Bullish FVG ... inside London Open Killzone after
     Asian High sweep, MSS confirmed, OTE 0.705" (text from `.resx`, time shown in NY);
   - the **ICT Pattern Chart** (§9.1) renders the candles with FVG/OB/sweep/MSS/OTE/killzone overlays and
     entry/stop/target lines, and live-updates;
   - a paper trade opens then closes; P&L is net of spread/commission/slippage/swap (§5.4);
   - the performance panel updates (win rate, R:R, drawdown, equity curve);
   - the **style filter** (Scalp/Intraday/Swing/Position) works.
6. To drive/observe the app, prefer the built-in `run` / `verify` skills.
