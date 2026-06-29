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
import { notifyNewOpportunity } from '../notifications/triggers';
import type {
  AlertDto,
  CandleDto,
  PaperTradeDto,
  PerformanceSummaryDto,
  RankedSignalDto,
  SetupDto,
} from '../types/api';
import type { ChartOverlay } from '../types/overlays';

export interface UseTradingHubArgs {
  hub?: TradingHubLike;
  symbol: string;
  timeframe: string;
  /**
   * The active trade style — part of the candles cache key (queryKeys.candles), so a live candle push
   * reconciles onto the SAME entry useCandles reads. Defaults to the §2.5 model style.
   */
  style?: string;
  /** Optionally synthesize an alert from a detected setup (the host also emits AlertDto directly). */
  alertFromSetup?: (setup: SetupDto) => AlertDto;
  /**
   * Optional side-effect on every TradeUpdated push, called with the RAW trade BEFORE the active-trades
   * merge drops a closed one — so a notification driver can observe an open→closed transition the
   * filtered cache never shows. Kept as a pass-through so the merge behaviour is unchanged.
   */
  onTradeEvent?: (trade: PaperTradeDto) => void;
}

export function useTradingHub({
  hub,
  symbol,
  timeframe,
  style = 'Intraday',
  alertFromSetup,
  onTradeEvent,
}: UseTradingHubArgs): void {
  const qc = useQueryClient();

  useEffect(() => {
    if (!hub) {
      return;
    }

    const dispose = bindTradingHub(hub, {
      onSetupDetected: (setup: SetupDto) => {
        qc.setQueryData<ChartOverlay[]>(queryKeys.overlays(symbol, timeframe), (prev) =>
          // Symbol AND timeframe must match the overlay cache key — a setup confirmed on another
          // timeframe must never pollute this chart's overlays (the key is symbol+timeframe).
          setup.symbol === symbol && setup.triggerTimeframe === timeframe
            ? mergeSetupOverlays(prev, setup)
            : (prev ?? []),
        );
        if (alertFromSetup) {
          const alert = alertFromSetup(setup);
          qc.setQueryData<AlertDto[]>(queryKeys.alerts(), (prev) => appendAlert(prev, alert));
        }
      },
      onTradeUpdated: (trade: PaperTradeDto) => {
        // Notify off the RAW push first (the merge below drops a closed trade from the active set, so a
        // close would otherwise be invisible to a cache-watching driver).
        onTradeEvent?.(trade);
        qc.setQueryData<PaperTradeDto[]>(queryKeys.activeTrades(), (prev) => upsertTrade(prev, trade));
      },
      onPerformanceUpdated: (summary: PerformanceSummaryDto) => {
        qc.setQueryData<PerformanceSummaryDto>(queryKeys.performance(), summary);
      },
      onCandleAppended: (candle: CandleDto) => {
        if (candle.symbol !== symbol || candle.timeframe !== timeframe) {
          return;
        }
        qc.setQueryData<CandleDto[]>(queryKeys.candles(symbol, timeframe, style), (prev) =>
          appendCandle(prev, candle),
        );
      },
      onSignalsUpdated: (signals: RankedSignalDto[]) => {
        // The push carries the FULL ranked top-N — replace the cache (mirrors PerformanceUpdated). Before
        // replacing, diff against the prior cache: any signal id that is NEW (a fresh opportunity) fires an
        // `opportunity` toast via the reserved notifyNewOpportunity hook.
        const prev = qc.getQueryData<RankedSignalDto[]>(queryKeys.signals()) ?? [];
        const knownIds = new Set(prev.map((s) => s.setup.id));
        for (const s of signals) {
          if (!knownIds.has(s.setup.id)) {
            notifyNewOpportunity({
              symbol: s.setup.symbol,
              title: `New signal — ${s.setup.symbol} ${s.setup.direction}`,
              body: `Grade ${s.setup.grade} · score ${s.score} · ${s.setup.style} ${s.setup.triggerTimeframe} · RR ${s.setup.rewardRatio}`,
              atUtc: s.setup.detectedAtUtc,
              focus: { symbol: s.setup.symbol, atUtc: s.setup.detectedAtUtc },
            });
          }
        }
        qc.setQueryData<RankedSignalDto[]>(queryKeys.signals(), signals);
      },
    });

    return dispose;
  }, [hub, qc, symbol, timeframe, style, alertFromSetup, onTradeEvent]);
}
