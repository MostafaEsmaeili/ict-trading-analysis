// ---------------------------------------------------------------------------------------------------
// React Query hooks — the dashboard's server-state owner (plan §9). ~30s reconcile poll; SignalR pushes
// merge into the same keys (see hub/useTradingHub). Mocks back every call until WP7 (api/client.ts).
// ---------------------------------------------------------------------------------------------------

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  fetchAccountStatus,
  fetchActiveTrades,
  fetchAlerts,
  fetchAllTrades,
  fetchBacktestDatasets,
  fetchChart,
  fetchConfig,
  fetchEquityCurve,
  fetchOverlays,
  fetchPerformance,
  fetchSettings,
  runBacktest,
  runOptimize,
  updateInstrumentSettings,
  type TradeFilters,
} from './client';
import type {
  BacktestRequest,
  BacktestResponse,
  InstrumentSettingsDto,
  OptimizeRequest,
  OptimizeResponse,
} from '../types/api';
import { queryKeys } from './queryKeys';

const RECONCILE_MS = 30_000;

export function useCandles(symbol: string, timeframe: string, style: string) {
  return useQuery({
    queryKey: queryKeys.candles(symbol, timeframe, style),
    queryFn: async () => (await fetchChart(symbol, timeframe, style)).candles,
    refetchInterval: RECONCILE_MS,
  });
}

/**
 * Overlay geometry. fetchOverlays consumes the SAME /api/chart ChartResponse the host already populates
 * with confirmed setups (mapped via setupToOverlays) — there is no dedicated overlay endpoint, so the
 * earlier always-throwing live path is gone. Style is part of the chart request; the result is filtered
 * to the selected timeframe (mirrors the SignalR merge guard) so polled + live pushes share the cache.
 */
export function useOverlays(symbol: string, timeframe: string, style: string) {
  return useQuery({
    queryKey: queryKeys.overlays(symbol, timeframe),
    queryFn: () => fetchOverlays(symbol, timeframe, style),
    refetchInterval: RECONCILE_MS,
  });
}

export function useAlerts() {
  return useQuery({
    queryKey: queryKeys.alerts(),
    queryFn: fetchAlerts,
    refetchInterval: RECONCILE_MS,
  });
}

export function useActiveTrades() {
  return useQuery({
    queryKey: queryKeys.activeTrades(),
    queryFn: fetchActiveTrades,
    refetchInterval: RECONCILE_MS,
  });
}

export function usePerformance() {
  return useQuery({
    queryKey: queryKeys.performance(),
    queryFn: fetchPerformance,
    refetchInterval: RECONCILE_MS,
  });
}

export function useEquityCurve() {
  return useQuery({
    queryKey: queryKeys.equityCurve(),
    queryFn: fetchEquityCurve,
    refetchInterval: RECONCILE_MS,
  });
}

/** The full trades history (open + closed) with optional server-side status/symbol filters (§15 §4). */
export function useAllTrades(filters: TradeFilters = {}) {
  return useQuery({
    queryKey: queryKeys.allTrades(filters.status, filters.symbol),
    queryFn: () => fetchAllTrades(filters),
    refetchInterval: RECONCILE_MS,
  });
}

/** The live paper-account snapshot (equity, open risk vs cap, streaks) — Live-page config panel (§15 §3). */
export function useAccountStatus() {
  return useQuery({
    queryKey: queryKeys.account(),
    queryFn: fetchAccountStatus,
    refetchInterval: RECONCILE_MS,
  });
}

/** The operator-visible runtime configuration (provider/symbols/styles/killzones/risk/costs) (§15 §3). */
export function useConfig() {
  return useQuery({
    queryKey: queryKeys.config(),
    queryFn: fetchConfig,
    refetchInterval: RECONCILE_MS,
  });
}

/** The live settings snapshot — per-instrument overrides + the read-only global concept settings (§15). */
export function useSettings() {
  return useQuery({
    queryKey: queryKeys.settings(),
    queryFn: fetchSettings,
    refetchInterval: RECONCILE_MS,
  });
}

/**
 * Set or clear one symbol's LIVE per-instrument override (PUT /api/settings/instruments/{symbol}). On
 * success it invalidates the settings query so the table re-reads the applied state — the change is live
 * (no restart), so the next backtest/scan already reflects it.
 */
export function useUpdateInstrumentSettings() {
  const qc = useQueryClient();
  return useMutation<void, Error, { symbol: string; body: InstrumentSettingsDto | null }>({
    mutationFn: ({ symbol, body }) => updateInstrumentSettings(symbol, body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: queryKeys.settings() });
    },
  });
}

/** The CSV history datasets available to the Backtest Lab + Optimizer (§15 §5/§6). */
export function useBacktestDatasets() {
  return useQuery({
    queryKey: queryKeys.backtestDatasets(),
    queryFn: fetchBacktestDatasets,
  });
}

/** Run a single backtest (POST /api/backtest). A mutation — results live in component state (§15 §5). */
export function useRunBacktest() {
  return useMutation<BacktestResponse, Error, BacktestRequest>({
    mutationFn: runBacktest,
  });
}

/** Run a parameter-grid optimization (POST /api/backtest/optimize) (§15 §6). */
export function useOptimize() {
  return useMutation<OptimizeResponse, Error, OptimizeRequest>({
    mutationFn: runOptimize,
  });
}
