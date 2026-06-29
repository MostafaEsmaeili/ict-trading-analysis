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
  fetchCalendar,
  fetchChart,
  fetchConfig,
  fetchEquityCurve,
  fetchMarketStatus,
  fetchOverlays,
  fetchPerformance,
  fetchSettings,
  fetchSignals,
  runBacktest,
  runOptimize,
  takeSignal,
  updateInstrumentSettings,
  type SignalFilters,
  type TakeSignalResult,
  type TradeFilters,
} from './client';
import type {
  BacktestRequest,
  BacktestResponse,
  InstrumentSettingsDto,
  OptimizeRequest,
  OptimizeResponse,
  PaperTradeDto,
  RankedSignalDto,
} from '../types/api';
import { upsertTrade } from './mergeDeltas';
import { notifyTradeOpened } from '../notifications/triggers';
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

/**
 * The ranked live signals top-N. SignalsUpdated pushes the full list onto the SAME key (queryKeys.signals)
 * so the live socket and the 30s reconcile share one cache; the page applies the client-side filters over
 * the cached data. Server filters (symbol/style/grade/max) are passed through to fetchSignals as needed.
 */
export function useSignals(filters: SignalFilters = {}) {
  return useQuery({
    queryKey: queryKeys.signals(),
    queryFn: () => fetchSignals(filters),
    refetchInterval: RECONCILE_MS,
  });
}

/**
 * Take a signal → open a PAPER trade (POST /api/signals/{setupId}/take). On success:
 *   - flip the signal's `isTaken` (+ a synthetic AlreadyTaken block) in the signals cache so its Take
 *     button disables immediately (optimistic-from-result, no separate optimistic write);
 *   - on a 200 (Immediate) merge the opened trade into the active-trades cache + fire a tradeOpened toast;
 *   - invalidate the account snapshot (reserved risk changed).
 * A 202 (Armed — a resting limit) opens nothing yet, so it only flips the signal + invalidates the account.
 * A 404/409 throws (the mutation's isError surfaces the `{ error }` reason). Paper only — no live order (§6.3).
 */
export function useTakeSignal() {
  const qc = useQueryClient();
  return useMutation<TakeSignalResult, Error, { setupId: string }>({
    mutationFn: ({ setupId }) => takeSignal(setupId),
    onSuccess: (result, { setupId }) => {
      // Flip the taken signal in the ranked cache (Take disables, "taken" reason shows).
      qc.setQueryData<RankedSignalDto[]>(queryKeys.signals(), (prev) =>
        (prev ?? []).map((s) =>
          s.setup.id === setupId
            ? { ...s, isTaken: true, blockReason: s.blockReason ?? 'AlreadyTaken' }
            : s,
        ),
      );
      // 200 (Immediate) returns the opened trade → merge it live + toast it. 202 (Armed) has no trade body.
      if (result.trade) {
        const trade: PaperTradeDto = result.trade;
        qc.setQueryData<PaperTradeDto[]>(queryKeys.activeTrades(), (prev) => upsertTrade(prev, trade));
        notifyTradeOpened(trade);
      }
      void qc.invalidateQueries({ queryKey: queryKeys.account() });
    },
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

/**
 * The live NY-session clock state (open/closed, current + next session, countdown) — the Live-page Market
 * Status widget. Polled on the same ~30s reconcile cadence; the widget interpolates the countdown locally
 * once a second between polls so the "opens in Xh Ym" stays smooth.
 */
export function useMarketStatus() {
  return useQuery({
    queryKey: queryKeys.marketStatus(),
    queryFn: fetchMarketStatus,
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

/** The economic-calendar status — enabled/loaded/provider, upcoming events, and §2.5.2 no-trade days (§2.5.8). */
export function useCalendar() {
  return useQuery({
    queryKey: queryKeys.calendar(),
    queryFn: fetchCalendar,
    refetchInterval: RECONCILE_MS,
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
