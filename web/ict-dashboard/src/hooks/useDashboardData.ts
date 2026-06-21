// ---------------------------------------------------------------------------------------------------
// useDashboardData — composes the §9 panel queries (candles / overlays / alerts / trades / performance
// / equity) for the current market selection and wires the inert SignalR hub (live host lands in WP7).
// Keeps all server-state orchestration out of the Dashboard shell. React Query owns the cache; the hub
// merges deltas into the SAME keys via setQueryData.
// ---------------------------------------------------------------------------------------------------

import { useMemo } from 'react';
import {
  useActiveTrades,
  useAlerts,
  useCandles,
  useEquityCurve,
  useOverlays,
  usePerformance,
} from '../api/hooks';
import { useTradingHub } from '../api/useTradingHub';
import type { Killzone, TradeStyle } from '../types/api';

export interface DashboardDataArgs {
  symbol: string;
  timeframe: string;
  style: TradeStyle;
}

export function useDashboardData({ symbol, timeframe, style }: DashboardDataArgs) {
  const candlesQ = useCandles(symbol, timeframe, style);
  const overlaysQ = useOverlays(symbol, timeframe);
  const alertsQ = useAlerts();
  const tradesQ = useActiveTrades();
  const perfQ = usePerformance();
  const equityQ = useEquityCurve();

  // SignalR wiring — inert without a live hub; the host connection lands in WP7.
  useTradingHub({ symbol, timeframe, style });

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
