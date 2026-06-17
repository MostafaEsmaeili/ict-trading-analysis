# ICT Automated Trading-Analysis System

A **defensive, paper-trading-only** system that translates the ICT (Inner Circle Trader)
methodology — mined from the course transcripts in this repo — into an automated market
scanner, alerter, internal paper-trading simulator, performance tracker, and a visual
OHLC dashboard that overlays the detected ICT concepts (FVG, order blocks, liquidity
sweeps, MSS, OTE, killzones).

## ⚠️ Guardrail: analysis + paper trading ONLY
This system must **never** place a live order with real capital. Live execution is made
*structurally impossible*, not flag-disabled: there is no broker/order interface anywhere,
the only `ITradeExecutor` is `SimulatedTradeExecutor`, all feeds are read-only/sandbox, and
a startup validator fails the app if live trading is ever enabled.

## Tech
.NET 10 · Modular Monolith with an in-memory message bus (no MediatR) · Domain-Driven Design ·
PostgreSQL + EF Core (JSONB) · SignalR · React + TypeScript with TradingView lightweight-charts ·
Reqnroll + Testcontainers E2E.

## Where to start
- **`docs/PLAN.md`** — the full implementation plan (source of truth): ICT rules + the mined
  2022-Mentorship entry model (§2.5), architecture (§3), features, trade-realism model (§5.4),
  data feeds (§6), tests (§8), the dashboard (§9), and the work-package roadmap (§11).
- **`CLAUDE.md`** — conventions for working in this repo.
- Transcripts: `2022 ICT Mentorship/`, `ICT Forex - Market Maker Primer Course/`.

Contributions follow an issue → branch → commit → PR flow (see `CLAUDE.md`).
