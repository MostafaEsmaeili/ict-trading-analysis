// ---------------------------------------------------------------------------------------------------
// React Query keys (plan §9). SignalR deltas merge into THESE exact keys via setQueryData, so the
// live push (WP7) and the polled snapshot reconcile on one cache entry. Keep them stable.
// ---------------------------------------------------------------------------------------------------

export const queryKeys = {
  candles: (symbol: string, timeframe: string, style: string) =>
    ['candles', symbol, timeframe, style] as const,
  overlays: (symbol: string, timeframe: string) => ['overlays', symbol, timeframe] as const,
  activeTrades: () => ['trades', 'active'] as const,
  // Full trades history, keyed by the (status, symbol) server filter so each filter caches distinctly.
  allTrades: (status?: string, symbol?: string) => ['trades', 'all', status ?? '', symbol ?? ''] as const,
  account: () => ['account'] as const,
  config: () => ['config'] as const,
  marketStatus: () => ['market-status'] as const,
  settings: () => ['settings'] as const,
  calendar: () => ['calendar'] as const,
  backtestDatasets: () => ['backtest', 'datasets'] as const,
  alerts: () => ['alerts'] as const,
  performance: () => ['performance'] as const,
  equityCurve: () => ['performance', 'equity'] as const,
} as const;
