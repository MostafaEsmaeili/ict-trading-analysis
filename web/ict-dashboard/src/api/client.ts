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
  AccountStatusDto,
  AlertDto,
  BacktestDatasetDto,
  BacktestRequest,
  BacktestResponse,
  CalendarStatusDto,
  ChartResponse,
  ConfigStatusDto,
  EquityPointDto,
  InstrumentSettingsDto,
  OptimizeRequest,
  OptimizeResponse,
  PaperTradeDto,
  PerformanceSummaryDto,
  SettingsDto,
} from '../types/api';
import type { ChartOverlay } from '../types/overlays';
import { setupToOverlays } from '../chart/setupToOverlays';
import {
  MOCK_ACCOUNT,
  MOCK_ACTIVE_TRADES,
  MOCK_ALERTS,
  MOCK_ALL_TRADES,
  MOCK_CANDLES,
  MOCK_CONFIG,
  MOCK_DATASETS,
  MOCK_EQUITY_CURVE,
  MOCK_OVERLAYS,
  MOCK_CALENDAR,
  MOCK_PERFORMANCE,
  MOCK_SETTINGS,
  mockBacktestResponse,
  mockOptimizeResponse,
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

/**
 * POST a JSON body and parse the JSON response. On a non-OK status the body may carry an
 * `{ error }` message (the backtest/optimize endpoints return 400/404 like that, plan §15 §5/§6) —
 * surface that exact message so the form shows WHY (a down/invalid request must look different from a
 * healthy one, §6.3). Falls back to the status code when no error body is present.
 */
async function postJson<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { accept: 'application/json', 'content-type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const detail = await readErrorDetail(res);
    throw new Error(detail || `POST ${path} → ${res.status}`);
  }
  return (await res.json()) as T;
}

/**
 * PUT a JSON body to an endpoint that returns 204 No Content on success (the settings mutation). On a
 * non-OK status the `{ error }` body carries the validation reason (e.g. a bad k-of-n or a subset missing
 * DisplacementMss) — surface it so the form shows WHY the save was rejected.
 */
async function putNoContent(path: string, body: unknown): Promise<void> {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'PUT',
    headers: { accept: 'application/json', 'content-type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const detail = await readErrorDetail(res);
    throw new Error(detail || `PUT ${path} → ${res.status}`);
  }
}

/** Best-effort read of an `{ error }` body from a non-OK response (empty string if absent/unparseable). */
async function readErrorDetail(res: Response): Promise<string> {
  try {
    const parsed = (await res.json()) as { error?: string };
    return typeof parsed?.error === 'string' ? parsed.error : '';
  } catch {
    return '';
  }
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

/** Filters for the full trades-history read (`GET /api/trades?status=&symbol=`). */
export interface TradeFilters {
  status?: 'Open' | 'Closed';
  symbol?: string;
}

/**
 * The full trades history (open + closed). `status`/`symbol` are server-side filters; omit both for
 * everything. In mocks mode the same filter is applied to the fixtures so the offline app behaves like
 * the live one. The richer client-side filtering (style / win-loss / sort) lives in the table.
 */
export async function fetchAllTrades(filters: TradeFilters = {}): Promise<PaperTradeDto[]> {
  if (USE_MOCKS) {
    return MOCK_ALL_TRADES.filter(
      (t) =>
        (!filters.status || t.status === filters.status) &&
        (!filters.symbol || t.symbol === filters.symbol),
    );
  }
  const params = new URLSearchParams();
  if (filters.status) params.set('status', filters.status);
  if (filters.symbol) params.set('symbol', filters.symbol);
  const qs = params.toString();
  return getJson<PaperTradeDto[]>(`/api/trades${qs ? `?${qs}` : ''}`);
}

export async function fetchAccountStatus(): Promise<AccountStatusDto> {
  if (USE_MOCKS) {
    return MOCK_ACCOUNT;
  }
  return getJson<AccountStatusDto>('/api/account');
}

export async function fetchConfig(): Promise<ConfigStatusDto> {
  if (USE_MOCKS) {
    return MOCK_CONFIG;
  }
  return getJson<ConfigStatusDto>('/api/config');
}

export async function fetchSettings(): Promise<SettingsDto> {
  if (USE_MOCKS) {
    return MOCK_SETTINGS;
  }
  return getJson<SettingsDto>('/api/settings');
}

/**
 * Set (body) or clear (null body) one symbol's LIVE per-instrument override. The change applies without a
 * restart (the runtime store bumps its revision; the scanner/orchestrator caches rebuild on the next
 * candle). In mocks mode it mutates the in-memory fixture (with the same DisplacementMss-required guard the
 * backend enforces) so the offline app behaves like the live one.
 */
export async function updateInstrumentSettings(
  symbol: string,
  body: InstrumentSettingsDto | null,
): Promise<void> {
  if (USE_MOCKS) {
    if (body === null) {
      delete MOCK_SETTINGS.instrumentOverrides[symbol];
      return;
    }
    if (body.requiredConditions && !body.requiredConditions.includes('DisplacementMss')) {
      throw new Error('A required subset must include DisplacementMss (the FSM direction lock).');
    }
    MOCK_SETTINGS.instrumentOverrides[symbol] = body;
    return;
  }
  return putNoContent(`/api/settings/instruments/${encodeURIComponent(symbol)}`, body);
}

export async function fetchCalendar(): Promise<CalendarStatusDto> {
  if (USE_MOCKS) {
    return MOCK_CALENDAR;
  }
  return getJson<CalendarStatusDto>('/api/calendar');
}

export async function fetchBacktestDatasets(): Promise<BacktestDatasetDto[]> {
  if (USE_MOCKS) {
    return MOCK_DATASETS;
  }
  return getJson<BacktestDatasetDto[]>('/api/backtest/datasets');
}

export async function runBacktest(req: BacktestRequest): Promise<BacktestResponse> {
  if (USE_MOCKS) {
    return mockBacktestResponse(
      req.symbol,
      req.style,
      req.startingBalance,
      req.riskPercent,
      req.timeframe,
    );
  }
  return postJson<BacktestResponse>('/api/backtest', req);
}

export async function runOptimize(req: OptimizeRequest): Promise<OptimizeResponse> {
  if (USE_MOCKS) {
    return mockOptimizeResponse(
      req.symbols,
      req.styles,
      req.riskPercents,
      req.startingBalance,
      req.objective,
      req.topN,
      req.timeframes,
    );
  }
  return postJson<OptimizeResponse>('/api/backtest/optimize', req);
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
  // The host now serves the equity curve over REST (GET /api/equity → EquityPointDto[]); the curve is
  // CUMULATIVE R from a zero baseline, not an account balance (see EquityPointDto). Hit it the same way
  // every other live read does — getJson FAILS HARD on a non-OK response (no silent fixture fallback).
  return getJson<EquityPointDto[]>('/api/equity');
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
