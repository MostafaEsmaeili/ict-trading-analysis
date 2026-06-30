// Trading hub binding test — proves bindTradingHub registers the four frozen handler names and routes a
// pushed payload to the typed callback, using a fake hub (no socket). The frozen names are asserted so a
// rename on the backend (TradingHub.cs) breaks this test.
import { describe, expect, it, vi } from 'vitest';
import { HubEvents, TRADING_HUB_ROUTE, bindTradingHub, type TradingHubLike } from './tradingHub';
import type { CandleDto, PaperTradeDto, PerformanceSummaryDto, SetupDto } from '../types/api';

function fakeHub() {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  const hub: TradingHubLike = {
    on: (event, handler) => handlers.set(event, handler),
    off: (event) => handlers.delete(event),
    start: async () => {},
    stop: async () => {},
    state: 'Disconnected' as TradingHubLike['state'],
  };
  return { hub, emit: (event: string, payload: unknown) => handlers.get(event)?.(payload), handlers };
}

describe('tradingHub', () => {
  it('exposes the frozen route and handler names', () => {
    expect(TRADING_HUB_ROUTE).toBe('/hubs/trading');
    expect(HubEvents).toEqual({
      SetupDetected: 'SetupDetected',
      TradeUpdated: 'TradeUpdated',
      PerformanceUpdated: 'PerformanceUpdated',
      CandleAppended: 'CandleAppended',
      SignalsUpdated: 'SignalsUpdated',
    });
  });

  it('routes each push to its typed handler and disposes cleanly', () => {
    const { hub, emit, handlers } = fakeHub();
    const onSetupDetected = vi.fn();
    const onTradeUpdated = vi.fn();
    const onPerformanceUpdated = vi.fn();
    const onCandleAppended = vi.fn();

    const dispose = bindTradingHub(hub, {
      onSetupDetected,
      onTradeUpdated,
      onPerformanceUpdated,
      onCandleAppended,
    });

    emit(HubEvents.SetupDetected, { id: 's' } as Partial<SetupDto>);
    emit(HubEvents.TradeUpdated, { id: 't' } as Partial<PaperTradeDto>);
    emit(HubEvents.PerformanceUpdated, { tradeCount: 1 } as Partial<PerformanceSummaryDto>);
    emit(HubEvents.CandleAppended, { symbol: 'EURUSD' } as Partial<CandleDto>);

    expect(onSetupDetected).toHaveBeenCalledWith({ id: 's' });
    expect(onTradeUpdated).toHaveBeenCalledWith({ id: 't' });
    expect(onPerformanceUpdated).toHaveBeenCalledWith({ tradeCount: 1 });
    expect(onCandleAppended).toHaveBeenCalledWith({ symbol: 'EURUSD' });

    dispose();
    expect(handlers.size).toBe(0);
  });
});
