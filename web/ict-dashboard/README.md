# ICT Dashboard (WP8)

The read-only ICT analysis + paper-trading dashboard (plan §9 / §9.1). React + TypeScript + Vite,
TradingView **lightweight-charts** (v5, OSS) for the ICT Pattern Chart, **Recharts** for the equity
curve, **React Query** for server state, and a **SignalR** client for live deltas.

> **Defensive guardrail.** This UI is analysis only. There is **no execute / go-live / order control
> anywhere** — every setup and trade is advisory paper (plan §6.3). The header shows an
> "Advisory · Paper only" posture badge; a test asserts no order button exists.

## Run

```bash
npm install
npm run dev        # http://localhost:5173 (proxies /api + /hubs to the .NET host on :5080)
npm run typecheck  # tsc --noEmit
npm run lint       # eslint (flat config)
npm run test       # vitest run
npm run build      # tsc -b && vite build
```

## Layout (plan §9)

A CSS grid that collapses below 1024px: **center** = the ICT Pattern Chart (candles + every §9.1
overlay, toggleable via the legend); **left** = the Alerts Feed (reasoning + killzone badge + direction
+ style chip); **right/bottom** = Active Paper Trades + the Performance panel (win rate, avg R, profit
factor, max DD, equity curve).

## Data (mocks now, live at WP7)

React Query owns server state on these keys: `['candles',sym,tf]`, `['overlays',sym,tf]`,
`['trades','active']`, `['alerts']`, `['performance']`. Every call (`src/api/client.ts`) is backed by a
deterministic mock fixture (`src/mocks/fixtures.ts`) so the dashboard renders fully on mocks. SignalR
pushes (`createTradingHub` / `useTradingHub`) merge deltas into the **same** keys via `setQueryData`;
the hook is inert until a hub is supplied (WP7 wires the host connection). Flip to live data with
`VITE_USE_MOCKS=false` once the host is up.

## Contract types

`src/types/api.ts` mirrors the frozen `contracts-v1` C# DTOs byte-for-byte (camelCase). The enum
member-name unions (`Direction` / `Killzone` / `TradeStyle` / `SetupGrade`) match the C# names exactly.
Regenerate the structural shapes from OpenAPI with `npm run gen:api` and reconcile (the host must emit
its OpenAPI document first — WP7).

## Time

UTC is the wire truth; the dashboard renders **New York** time by default (labelled "NY"), DST-aware via
`date-fns-tz` + `America/New_York` (`src/time.ts`).
