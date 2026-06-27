// ---------------------------------------------------------------------------------------------------
// useDashboardData — composes the §9 panel queries (candles / overlays / alerts / trades / performance
// / equity) for the current market selection and owns the live SignalR hub lifecycle. In a live build
// (VITE_USE_MOCKS=false) it connects /hubs/trading with withAutomaticReconnect and merges each pushed
// delta into the SAME React Query keys via setQueryData; in mocks mode the hub is absent (socket-free)
// and the panels reconcile on the 30s poll. Keeps all server-state orchestration out of the shell.
// ---------------------------------------------------------------------------------------------------

import { useEffect, useMemo } from 'react';
import {
  useActiveTrades,
  useAlerts,
  useCandles,
  useEquityCurve,
  useOverlays,
  usePerformance,
} from '../api/hooks';
import { useTradingHub } from '../api/useTradingHub';
import { createTradingHub, type TradingHubLike } from '../api/tradingHub';
import { API_BASE, USE_MOCKS } from '../api/client';
import type { Killzone, TradeStyle } from '../types/api';

export interface DashboardDataArgs {
  symbol: string;
  timeframe: string;
  style: TradeStyle;
}

export function useDashboardData({ symbol, timeframe, style }: DashboardDataArgs) {
  const candlesQ = useCandles(symbol, timeframe, style);
  const overlaysQ = useOverlays(symbol, timeframe, style);
  const alertsQ = useAlerts();
  const tradesQ = useActiveTrades();
  const perfQ = usePerformance();
  const equityQ = useEquityCurve();

  // Live SignalR hub — built ONCE for the app's lifetime, only in a live build (mocks mode stays
  // socket-free). The connection reaches /hubs/trading through the Vite proxy (same origin in dev).
  // Lifecycle: start on mount, stop on unmount. useTradingHub binds the push handlers and merges each
  // delta into the matching React Query cache via setQueryData (candles/overlays/trades/performance).
  const hub = useMemo<TradingHubLike | undefined>(
    () => (USE_MOCKS ? undefined : createTradingHub(API_BASE)),
    [],
  );

  useEffect(() => {
    if (!hub) {
      return;
    }
    // start()/stop() are async; the hook owns the lifecycle. A reconnect is handled by
    // withAutomaticReconnect inside createTradingHub. Ignore a start race on an unmount-before-connect.
    void hub.start().catch(() => undefined);
    return () => {
      void hub.stop().catch(() => undefined);
    };
  }, [hub]);

  // SignalR wiring — inert in mocks mode (no hub); live the hub above feeds deltas into the cache.
  useTradingHub({ hub, symbol, timeframe, style });

  // The killzone of the latest alert on the focused symbol (drives the chart-header killzone badge).
  const activeKillzone = useMemo<Killzone | null>(
    () =>
      ((alertsQ.data ?? []).find((a) => a.symbol === symbol)?.killzone as Killzone | null) ?? null,
    [alertsQ.data, symbol],
  );

  // Trigger TF for the header badge: present only when an alert exists for the focused symbol.
  const triggerTimeframe = useMemo<string | null>(() => {
    const latest = (alertsQ.data ?? []).find((a) => a.symbol === symbol);
    return latest ? timeframe : null;
  }, [alertsQ.data, symbol, timeframe]);

  return { candlesQ, overlaysQ, alertsQ, tradesQ, perfQ, equityQ, activeKillzone, triggerTimeframe };
}
