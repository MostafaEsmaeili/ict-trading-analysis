// useTradingHub wiring test — proves the SignalR push → setQueryData merge respects the overlay cache
// key's FULL identity (symbol AND timeframe). Regression guard for CodeRabbit #34: a setup confirmed on
// a different timeframe must NOT pollute the current chart's overlays (key = symbol + timeframe).
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { createElement } from 'react';
import { useTradingHub } from './useTradingHub';
import { queryKeys } from './queryKeys';
import type { TradingHubLike } from './tradingHub';
import { HubEvents } from './tradingHub';
import type { ChartOverlay } from '../types/overlays';
import type {
  CandleDto,
  PaperTradeDto,
  PerformanceSummaryDto,
  RankedSignalDto,
  SetupDto,
} from '../types/api';
import { __resetNotificationsForTest, getSnapshot } from '../notifications/notificationStore';

function fakeHub() {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  const hub: TradingHubLike = {
    on: (event, handler) => handlers.set(event, handler),
    off: (event) => handlers.delete(event),
    start: async () => {},
    stop: async () => {},
    state: 'Disconnected' as TradingHubLike['state'],
  };
  return { hub, emit: (event: string, payload: unknown) => handlers.get(event)?.(payload) };
}

function setup(triggerTimeframe: string): SetupDto {
  return {
    id: 's1',
    symbol: 'EURUSD',
    direction: 'Bullish',
    killzone: 'LondonOpen',
    style: 'Intraday',
    grade: 'A',
    score: 82,
    triggerTimeframe,
    entry: 1.07,
    stop: 1.069,
    targets: [1.072, 1.074],
    rewardRatio: 2,
    reason: 'test',
    detectedAtUtc: '2026-06-19T06:00:00Z',
    isAdvisoryOnly: true,
  };
}

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('useTradingHub overlay merge', () => {
  it('merges a setup whose symbol AND timeframe match the chart', () => {
    const qc = new QueryClient();
    const { hub, emit } = fakeHub();
    renderHook(() => useTradingHub({ hub, symbol: 'EURUSD', timeframe: 'M5' }), {
      wrapper: makeWrapper(qc),
    });

    emit(HubEvents.SetupDetected, setup('M5'));

    const overlays = qc.getQueryData<ChartOverlay[]>(queryKeys.overlays('EURUSD', 'M5'));
    expect(overlays?.length ?? 0).toBeGreaterThan(0);
  });

  it('does NOT merge a setup from a different timeframe (no cross-timeframe pollution)', () => {
    const qc = new QueryClient();
    const { hub, emit } = fakeHub();
    renderHook(() => useTradingHub({ hub, symbol: 'EURUSD', timeframe: 'M5' }), {
      wrapper: makeWrapper(qc),
    });

    emit(HubEvents.SetupDetected, setup('H1'));

    const overlays = qc.getQueryData<ChartOverlay[]>(queryKeys.overlays('EURUSD', 'M5'));
    expect(overlays ?? []).toEqual([]);
  });

  it('merges a live trade update into the active-trades cache', () => {
    const qc = new QueryClient();
    const { hub, emit } = fakeHub();
    renderHook(() => useTradingHub({ hub, symbol: 'EURUSD', timeframe: 'M5' }), {
      wrapper: makeWrapper(qc),
    });

    const trade: PaperTradeDto = {
      id: 'tr1',
      setupId: 's1',
      symbol: 'EURUSD',
      direction: 'Long',
      status: 'Open',
      style: 'Intraday',
      killzone: 'LondonOpen',
      entry: 1.07,
      stop: 1.069,
      targets: [1.072],
      size: 0.5,
      openedAtUtc: '2026-06-19T06:00:00Z',
      closedAtUtc: null,
      realizedR: null,
      lifecycle: 'Open',
      closeReason: null,
      netR: null,
      grossPnl: null,
      costs: null,
      netPnl: null,
      hasScaledOut: false,
      isBreakevenArmed: false,
      riskBudget: 100,
      timeframe: 'M5',
      currentStop: 1.069,
      exitPrice: null,
      managedFromUtc: '2026-06-19T06:00:00Z',
    };
    emit(HubEvents.TradeUpdated, trade);

    const trades = qc.getQueryData<PaperTradeDto[]>(queryKeys.activeTrades());
    expect(trades?.map((t) => t.id)).toEqual(['tr1']);
  });

  it('merges a live performance summary into the performance cache', () => {
    const qc = new QueryClient();
    const { hub, emit } = fakeHub();
    renderHook(() => useTradingHub({ hub, symbol: 'EURUSD', timeframe: 'M5' }), {
      wrapper: makeWrapper(qc),
    });

    const summary: PerformanceSummaryDto = {
      tradeCount: 7,
      winRate: 0.57,
      averageR: 1.1,
      profitFactor: 2,
      expectancy: 0.6,
      maxDrawdown: 2.4,
    };
    emit(HubEvents.PerformanceUpdated, summary);

    expect(qc.getQueryData<PerformanceSummaryDto>(queryKeys.performance())).toEqual(summary);
  });

  it('merges a live candle (matching symbol+timeframe) into the candles cache', () => {
    const qc = new QueryClient();
    const { hub, emit } = fakeHub();
    renderHook(() => useTradingHub({ hub, symbol: 'EURUSD', timeframe: 'M5', style: 'Intraday' }), {
      wrapper: makeWrapper(qc),
    });

    const candle: CandleDto = {
      symbol: 'EURUSD',
      timeframe: 'M5',
      openTimeUtc: '2026-06-19T06:00:00Z',
      open: 1.07,
      high: 1.071,
      low: 1.069,
      close: 1.0705,
      volume: 100,
    };
    emit(HubEvents.CandleAppended, candle);

    const candles = qc.getQueryData<CandleDto[]>(queryKeys.candles('EURUSD', 'M5', 'Intraday'));
    expect(candles).toHaveLength(1);

    // A candle for a DIFFERENT timeframe is ignored (no cross-timeframe pollution).
    emit(HubEvents.CandleAppended, { ...candle, timeframe: 'H1' });
    expect(
      qc.getQueryData<CandleDto[]>(queryKeys.candles('EURUSD', 'M5', 'Intraday')),
    ).toHaveLength(1);
  });
});

function rankedSignal(id: string, rank: number): RankedSignalDto {
  return {
    rank,
    score: 80,
    entryMode: 'Manual',
    isTaken: false,
    blockReason: null,
    expiresAtUtc: null,
    setup: { ...setup('M15'), id },
  };
}

describe('useTradingHub SignalsUpdated', () => {
  beforeEach(() => __resetNotificationsForTest());
  afterEach(() => __resetNotificationsForTest());

  it('replaces the signals cache with the pushed ranked top-N', () => {
    const qc = new QueryClient();
    const { hub, emit } = fakeHub();
    renderHook(() => useTradingHub({ hub, symbol: 'EURUSD', timeframe: 'M5' }), {
      wrapper: makeWrapper(qc),
    });

    qc.setQueryData<RankedSignalDto[]>(queryKeys.signals(), [rankedSignal('old', 1)]);
    emit(HubEvents.SignalsUpdated, [rankedSignal('new-a', 1), rankedSignal('new-b', 2)]);

    const list = qc.getQueryData<RankedSignalDto[]>(queryKeys.signals());
    expect(list?.map((s) => s.setup.id)).toEqual(['new-a', 'new-b']);
  });

  it('fires an opportunity notification for a NEW signal id (vs the prior cache)', () => {
    const qc = new QueryClient();
    const { hub, emit } = fakeHub();
    renderHook(() => useTradingHub({ hub, symbol: 'EURUSD', timeframe: 'M5' }), {
      wrapper: makeWrapper(qc),
    });

    // Seed a known signal; pushing it again + a fresh one should toast ONLY the fresh one.
    qc.setQueryData<RankedSignalDto[]>(queryKeys.signals(), [rankedSignal('known', 1)]);
    emit(HubEvents.SignalsUpdated, [rankedSignal('known', 1), rankedSignal('fresh', 2)]);

    const opportunities = getSnapshot().filter((n) => n.kind === 'opportunity');
    expect(opportunities).toHaveLength(1);
    expect(opportunities[0].title).toMatch(/new signal/i);
  });
});
