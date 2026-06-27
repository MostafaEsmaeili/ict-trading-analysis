// ---------------------------------------------------------------------------------------------------
// useMarketSelection — the operator's chart selection (symbol / timeframe / trade style). Owns the
// selection state + the frozen-style guard so the Dashboard shell stays presentational. The style is
// constrained to the frozen TradeStyle union (a stray non-frozen string is ignored).
// ---------------------------------------------------------------------------------------------------

import { useCallback, useState } from 'react';
import type { TradeStyle } from '../types/api';
import { STYLES } from '../components/ChartPanel';
import { MOCK_SYMBOL, MOCK_TIMEFRAME } from '../mocks/fixtures';

export interface MarketSelection {
  symbol: string;
  timeframe: string;
  style: TradeStyle;
  setSymbol: (symbol: string) => void;
  setTimeframe: (timeframe: string) => void;
  /** Selects a style only if it is one of the frozen TradeStyle members (guards a stray string). */
  selectStyle: (next: TradeStyle) => void;
}

/**
 * @param initialSymbol seeds the symbol once (e.g. from a `?symbol=` deep-link off the Trades page);
 *        defaults to the mock symbol. Only the initial render reads it — the operator's later switches win.
 */
export function useMarketSelection(initialSymbol?: string): MarketSelection {
  const [symbol, setSymbol] = useState<string>(initialSymbol ?? MOCK_SYMBOL);
  const [timeframe, setTimeframe] = useState<string>(MOCK_TIMEFRAME);
  const [style, setStyle] = useState<TradeStyle>('Intraday');

  const selectStyle = useCallback((next: TradeStyle) => {
    // Guard against a stray non-frozen style string.
    if (STYLES.includes(next)) {
      setStyle(next);
    }
  }, []);

  return { symbol, timeframe, style, setSymbol, setTimeframe, selectStyle };
}
