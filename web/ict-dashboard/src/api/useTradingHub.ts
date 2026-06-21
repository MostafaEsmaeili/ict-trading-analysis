// ---------------------------------------------------------------------------------------------------
// useTradingHub — wires the SignalR push handlers into React Query (plan §9). New setups/trades/perf/
// candles stream onto the chart + panels live via setQueryData, so the live socket and the polled
// snapshot share one cache. No live connection is required: if `hub` is omitted the hook is inert
// (the component tests run without a socket), and the host wiring lands with WP7.
// ---------------------------------------------------------------------------------------------------

import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { queryKeys } from './queryKeys';
import { bindTradingHub, type TradingHubLike } from './tradingHub';
import {
  appendAlert,
  appendCandle,
  mergeSetupOverlays,
  upsertTrade,
} from './mergeDeltas';
import type { AlertDto, CandleDto, PaperTradeDto, PerformanceSummaryDto, SetupDto } from '../types/api';
import type { ChartOverlay } from '../types/overlays';

export interface UseTradingHubArgs {
  hub?: TradingHubLike;
  symbol: string;
  timeframe: string;
  /** Optionally synthesize an alert from a detected setup (the host also emits AlertDto directly). */
  alertFromSetup?: (setup: SetupDto) => AlertDto;
}

export function useTradingHub({ hub, symbol, timeframe, alertFromSetup }: UseTradingHubArgs): void {
  const qc = useQueryClient();

  useEffect(() => {
    if (!hub) {
      return;
    }

    const dispose = bindTradingHub(hub, {
      onSetupDetected: (setup: SetupDto) => {
        qc.setQueryData<ChartOverlay[]>(queryKeys.overlays(symbol, timeframe), (prev) =>
          setup.symbol === symbol ? mergeSetupOverlays(prev, setup) : (prev ?? []),
        );
        if (alertFromSetup) {
          const alert = alertFromSetup(setup);
          qc.setQueryData<AlertDto[]>(queryKeys.alerts(), (prev) => appendAlert(prev, alert));
        }
      },
      onTradeUpdated: (trade: PaperTradeDto) => {
        qc.setQueryData<PaperTradeDto[]>(queryKeys.activeTrades(), (prev) => upsertTrade(prev, trade));
      },
      onPerformanceUpdated: (summary: PerformanceSummaryDto) => {
        qc.setQueryData<PerformanceSummaryDto>(queryKeys.performance(), summary);
      },
      onCandleAppended: (candle: CandleDto) => {
        if (candle.symbol !== symbol || candle.timeframe !== timeframe) {
          return;
        }
        qc.setQueryData<CandleDto[]>(queryKeys.candles(symbol, timeframe), (prev) =>
          appendCandle(prev, candle),
        );
      },
    });

    return dispose;
  }, [hub, qc, symbol, timeframe, alertFromSetup]);
}
