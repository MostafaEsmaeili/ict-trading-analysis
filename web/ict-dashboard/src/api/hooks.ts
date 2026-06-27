// ---------------------------------------------------------------------------------------------------
// React Query hooks — the dashboard's server-state owner (plan §9). ~30s reconcile poll; SignalR pushes
// merge into the same keys (see hub/useTradingHub). Mocks back every call until WP7 (api/client.ts).
// ---------------------------------------------------------------------------------------------------

import { useQuery } from '@tanstack/react-query';
import {
  fetchActiveTrades,
  fetchAlerts,
  fetchChart,
  fetchEquityCurve,
  fetchOverlays,
  fetchPerformance,
} from './client';
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
