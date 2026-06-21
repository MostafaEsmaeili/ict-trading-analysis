// useTradingHub wiring test — proves the SignalR push → setQueryData merge respects the overlay cache
// key's FULL identity (symbol AND timeframe). Regression guard for CodeRabbit #34: a setup confirmed on
// a different timeframe must NOT pollute the current chart's overlays (key = symbol + timeframe).
import { describe, expect, it } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { createElement } from 'react';
import { useTradingHub } from './useTradingHub';
import { queryKeys } from './queryKeys';
import type { TradingHubLike } from './tradingHub';
import { HubEvents } from './tradingHub';
import type { ChartOverlay } from '../types/overlays';
import type { SetupDto } from '../types/api';

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
});
