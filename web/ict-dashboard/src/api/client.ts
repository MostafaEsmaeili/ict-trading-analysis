// ---------------------------------------------------------------------------------------------------
// REST client for the frozen surface (plan §11.1 #6). One clean switch, NO partial fallback:
//
//   VITE_USE_MOCKS=true  (default, until WP7) → ALWAYS serve the deterministic mock fixtures. The host
//                          isn't wired yet, so this keeps the dashboard fully renderable on mocks
//                          (plan §11.3 Phase B) with the real DTO shapes.
//   VITE_USE_MOCKS=false (live, WP7+)         → hit the real API and FAIL HARD on any error. We do NOT
//                          silently serve fake data in a live build — masking a down host with fixtures
//                          would show fabricated setups/trades as if real, the opposite of the §6.3
//                          defensive posture. A failed fetch surfaces as a query error in the UI.
//
// The earlier "empty successful response → fixtures" path is removed: it conflated "host returned no
// data" (a legitimate empty state) with "use mocks", which is exactly the silent-fake-data trap above.
// ---------------------------------------------------------------------------------------------------

import type {
  AlertDto,
  ChartResponse,
  EquityPointDto,
  PaperTradeDto,
  PerformanceSummaryDto,
} from '../types/api';
import type { ChartOverlay } from '../types/overlays';
import { setupToOverlays } from '../chart/setupToOverlays';
import {
  MOCK_ACTIVE_TRADES,
  MOCK_ALERTS,
  MOCK_CANDLES,
  MOCK_EQUITY_CURVE,
  MOCK_OVERLAYS,
  MOCK_PERFORMANCE,
} from '../mocks/fixtures';

export const API_BASE = import.meta.env.VITE_API_BASE ?? '';

// Default to mocks until the live host (WP7) is wired. An explicit "false" opts into real fetches.
// Exported so the live SignalR hub gates on the SAME switch (mocks mode stays socket-free).
export const USE_MOCKS = (import.meta.env.VITE_USE_MOCKS ?? 'true') !== 'false';

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { accept: 'application/json' },
  });
  if (!res.ok) {
    throw new Error(`GET ${path} → ${res.status}`);
  }
  return (await res.json()) as T;
}

export async function fetchAlerts(): Promise<AlertDto[]> {
  if (USE_MOCKS) {
    return MOCK_ALERTS;
  }
  return getJson<AlertDto[]>('/api/alerts');
}

export async function fetchActiveTrades(): Promise<PaperTradeDto[]> {
  if (USE_MOCKS) {
    return MOCK_ACTIVE_TRADES;
  }
  return getJson<PaperTradeDto[]>('/api/trades/active');
}

export async function fetchPerformance(): Promise<PerformanceSummaryDto> {
  if (USE_MOCKS) {
    return MOCK_PERFORMANCE;
  }
  return getJson<PerformanceSummaryDto>('/api/performance');
}

export async function fetchEquityCurve(): Promise<EquityPointDto[]> {
  if (USE_MOCKS) {
    return MOCK_EQUITY_CURVE;
  }
  // No dedicated REST endpoint in contracts-v1 yet (GetEquityCurveQuery is bus-only); the host wires
  // it in WP7. Until then a live build has nowhere to fetch it — surface that rather than fake it.
  throw new Error('Equity-curve endpoint is not available until WP7 (GetEquityCurveQuery is bus-only).');
}

export async function fetchChart(
  symbol: string,
  timeframe: string,
  style: string,
): Promise<ChartResponse> {
  if (USE_MOCKS) {
    return { symbol, timeframe, style, candles: MOCK_CANDLES, overlays: [] };
  }
  const params = new URLSearchParams({ tf: timeframe, style });
  return getJson<ChartResponse>(`/api/chart/${symbol}?${params}`);
}

// Overlay geometry. There is NO dedicated overlay endpoint in contracts-v1 — overlays ride the
// ChartResponse the host already returns (and SignalR pushes). So the LIVE path fetches the same
// /api/chart response and maps its confirmed setups via setupToOverlays, filtered to the requested
// timeframe (a setup confirmed on another TF must not pollute this chart). Mock mode serves the rich
// §9.1 fixture so every overlay primitive is exercised in the scaffold.
export async function fetchOverlays(
  symbol: string,
  timeframe: string,
  style = 'Intraday',
): Promise<ChartOverlay[]> {
  if (USE_MOCKS) {
    return MOCK_OVERLAYS;
  }
  const chart = await fetchChart(symbol, timeframe, style);
  return chart.overlays.filter((s) => s.triggerTimeframe === timeframe).flatMap(setupToOverlays);
}
