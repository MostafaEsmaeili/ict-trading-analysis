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
import {
  MOCK_ACTIVE_TRADES,
  MOCK_ALERTS,
  MOCK_CANDLES,
  MOCK_EQUITY_CURVE,
  MOCK_OVERLAYS,
  MOCK_PERFORMANCE,
} from '../mocks/fixtures';

const API_BASE = import.meta.env.VITE_API_BASE ?? '';

// Default to mocks until the live host (WP7) is wired. An explicit "false" opts into real fetches.
const USE_MOCKS = (import.meta.env.VITE_USE_MOCKS ?? 'true') !== 'false';

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

// Overlay geometry is derived from a Setup's Evidence by the host (plan §9.1). Until WP7 emits that,
// the chart draws from the mock overlay fixture so every §9.1 primitive is exercised.
export async function fetchOverlays(_symbol: string, _timeframe: string): Promise<ChartOverlay[]> {
  if (USE_MOCKS) {
    return MOCK_OVERLAYS;
  }
  // No dedicated REST endpoint in contracts-v1 yet — overlays ride the ChartResponse + SignalR pushes.
  throw new Error('Overlay endpoint is not available until WP7 (overlays ride ChartResponse / SignalR).');
}
