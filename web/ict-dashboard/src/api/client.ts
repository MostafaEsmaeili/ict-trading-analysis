// ---------------------------------------------------------------------------------------------------
// REST client for the frozen surface (plan §11.1 #6). Until WP7 the host returns typed-empty results,
// so each call falls back to the deterministic mock fixture when the response is empty or unreachable.
// This keeps the dashboard fully renderable on mocks (plan §11.3 Phase B) yet ready to flip to live
// data with no shape changes. Set VITE_USE_MOCKS=false once WP7 lands.
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
  const live = await getJson<AlertDto[]>('/api/alerts');
  return live.length > 0 ? live : MOCK_ALERTS;
}

export async function fetchActiveTrades(): Promise<PaperTradeDto[]> {
  if (USE_MOCKS) {
    return MOCK_ACTIVE_TRADES;
  }
  const live = await getJson<PaperTradeDto[]>('/api/trades/active');
  return live.length > 0 ? live : MOCK_ACTIVE_TRADES;
}

export async function fetchPerformance(): Promise<PerformanceSummaryDto> {
  if (USE_MOCKS) {
    return MOCK_PERFORMANCE;
  }
  const live = await getJson<PerformanceSummaryDto>('/api/performance');
  return live.tradeCount > 0 ? live : MOCK_PERFORMANCE;
}

export async function fetchEquityCurve(): Promise<EquityPointDto[]> {
  // No dedicated endpoint in contracts-v1 yet (GetEquityCurveQuery is bus-only); mock for now.
  return MOCK_EQUITY_CURVE;
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
  const live = await getJson<ChartResponse>(`/api/chart/${symbol}?${params}`);
  return live.candles.length > 0 ? live : { symbol, timeframe, style, candles: MOCK_CANDLES, overlays: [] };
}

// Overlay geometry is derived from a Setup's Evidence by the host (plan §9.1). Until WP7 emits that,
// the chart draws from the mock overlay fixture so every §9.1 primitive is exercised.
export async function fetchOverlays(_symbol: string, _timeframe: string): Promise<ChartOverlay[]> {
  return MOCK_OVERLAYS;
}
