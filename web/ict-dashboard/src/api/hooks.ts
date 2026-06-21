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
    queryKey: queryKeys.candles(symbol, timeframe),
    queryFn: async () => (await fetchChart(symbol, timeframe, style)).candles,
    refetchInterval: RECONCILE_MS,
  });
}

export function useOverlays(symbol: string, timeframe: string) {
  return useQuery({
    queryKey: queryKeys.overlays(symbol, timeframe),
    queryFn: () => fetchOverlays(symbol, timeframe),
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
