// ---------------------------------------------------------------------------------------------------
// React Query keys (plan §9). SignalR deltas merge into THESE exact keys via setQueryData, so the
// live push (WP7) and the polled snapshot reconcile on one cache entry. Keep them stable.
// ---------------------------------------------------------------------------------------------------

export const queryKeys = {
  candles: (symbol: string, timeframe: string) => ['candles', symbol, timeframe] as const,
  overlays: (symbol: string, timeframe: string) => ['overlays', symbol, timeframe] as const,
  activeTrades: () => ['trades', 'active'] as const,
  alerts: () => ['alerts'] as const,
  performance: () => ['performance'] as const,
  equityCurve: () => ['performance', 'equity'] as const,
} as const;
