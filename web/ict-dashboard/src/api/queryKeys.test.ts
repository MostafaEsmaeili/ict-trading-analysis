// queryKeys contract — the cache keys are the reconciliation point for the polled snapshot AND the
// SignalR deltas (setQueryData writes to these exact keys), so they must stay stable and must capture
// every input that changes the fetched data. Regression guard for the candles-key style collision
// (CodeRabbit #34): candles are fetched by (symbol, timeframe, style) and MUST cache by all three.
import { describe, expect, it } from 'vitest';
import { queryKeys } from './queryKeys';

describe('queryKeys', () => {
  it('keys candles by symbol, timeframe AND style', () => {
    expect(queryKeys.candles('EURUSD', 'M5', 'Intraday')).toEqual([
      'candles',
      'EURUSD',
      'M5',
      'Intraday',
    ]);
  });

  it('produces a DISTINCT candles key per style (no stale cross-style reuse)', () => {
    const intraday = queryKeys.candles('EURUSD', 'M5', 'Intraday');
    const scalp = queryKeys.candles('EURUSD', 'M5', 'Scalp');
    expect(intraday).not.toEqual(scalp);
  });

  it('keys overlays by symbol + timeframe', () => {
    expect(queryKeys.overlays('EURUSD', 'M5')).toEqual(['overlays', 'EURUSD', 'M5']);
  });

  it('exposes stable singleton keys for the global queries', () => {
    expect(queryKeys.activeTrades()).toEqual(['trades', 'active']);
    expect(queryKeys.alerts()).toEqual(['alerts']);
    expect(queryKeys.performance()).toEqual(['performance']);
    expect(queryKeys.equityCurve()).toEqual(['performance', 'equity']);
  });
});
