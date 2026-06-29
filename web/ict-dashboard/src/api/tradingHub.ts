// ---------------------------------------------------------------------------------------------------
// SignalR trading hub client (plan §9 / §11.1 #6). FROZEN: route `/hubs/trading` and the four push
// handler names SetupDetected / TradeUpdated / PerformanceUpdated / CandleAppended (TradingHub.cs). The
// hub is push-only — there is deliberately NO inbound "execute"/"order" method, so the defensive
// guardrail holds at the transport layer too (plan §6.3). `withAutomaticReconnect`.
//
// `createTradingHub` builds a connection but does NOT start it (the caller owns the lifecycle); tests
// pass a fake connection. No live connection is required for the component tests to pass.
// ---------------------------------------------------------------------------------------------------

import {
  HubConnectionBuilder,
  HubConnectionState,
  type HubConnection,
} from '@microsoft/signalr';
import type {
  AlertDto,
  CandleDto,
  PaperTradeDto,
  PerformanceSummaryDto,
  RankedSignalDto,
  SetupDto,
} from '../types/api';

export const TRADING_HUB_ROUTE = '/hubs/trading';

/** The server→client push handler names (frozen — TradingHub.cs). */
export const HubEvents = {
  SetupDetected: 'SetupDetected',
  TradeUpdated: 'TradeUpdated',
  PerformanceUpdated: 'PerformanceUpdated',
  CandleAppended: 'CandleAppended',
  /** The full ranked signals top-N (RankedSignalDto[]) — pushed whenever the live ranking changes. */
  SignalsUpdated: 'SignalsUpdated',
} as const;

/** The minimal surface useTradingHub needs — lets tests substitute a fake (no real socket). */
export interface TradingHubLike {
  on(event: string, handler: (...args: unknown[]) => void): void;
  off(event: string): void;
  start(): Promise<void>;
  stop(): Promise<void>;
  state: HubConnectionState;
}

/** Typed payloads pushed on each handler (mirror the bus events the host relays). */
export interface TradingHubHandlers {
  onSetupDetected?: (setup: SetupDto) => void;
  onTradeUpdated?: (trade: PaperTradeDto) => void;
  onPerformanceUpdated?: (summary: PerformanceSummaryDto) => void;
  onCandleAppended?: (candle: CandleDto) => void;
  onAlert?: (alert: AlertDto) => void;
  /** The full ranked signals top-N replaced the cache (the signals feed re-renders from it). */
  onSignalsUpdated?: (signals: RankedSignalDto[]) => void;
}

/**
 * Build (but do not start) a SignalR connection to the trading hub. Adapts HubConnection to
 * TradingHubLike so callers/tests can treat both uniformly.
 */
export function createTradingHub(baseUrl: string): TradingHubLike {
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(`${baseUrl}${TRADING_HUB_ROUTE}`)
    .withAutomaticReconnect()
    .build();

  return {
    on: (event, handler) => connection.on(event, handler),
    off: (event) => connection.off(event),
    start: () => connection.start(),
    stop: () => connection.stop(),
    get state() {
      return connection.state;
    },
  };
}

/**
 * Register the typed handlers against a hub (real or fake) and return a disposer. Pure wiring — no
 * connection lifecycle here, so it is trivially unit-testable with a fake hub.
 */
export function bindTradingHub(hub: TradingHubLike, handlers: TradingHubHandlers): () => void {
  if (handlers.onSetupDetected) {
    hub.on(HubEvents.SetupDetected, (s) => handlers.onSetupDetected?.(s as SetupDto));
  }
  if (handlers.onTradeUpdated) {
    hub.on(HubEvents.TradeUpdated, (tr) => handlers.onTradeUpdated?.(tr as PaperTradeDto));
  }
  if (handlers.onPerformanceUpdated) {
    hub.on(HubEvents.PerformanceUpdated, (p) =>
      handlers.onPerformanceUpdated?.(p as PerformanceSummaryDto),
    );
  }
  if (handlers.onCandleAppended) {
    hub.on(HubEvents.CandleAppended, (c) => handlers.onCandleAppended?.(c as CandleDto));
  }
  if (handlers.onSignalsUpdated) {
    hub.on(HubEvents.SignalsUpdated, (s) => handlers.onSignalsUpdated?.(s as RankedSignalDto[]));
  }

  return () => {
    hub.off(HubEvents.SetupDetected);
    hub.off(HubEvents.TradeUpdated);
    hub.off(HubEvents.PerformanceUpdated);
    hub.off(HubEvents.CandleAppended);
    hub.off(HubEvents.SignalsUpdated);
  };
}

export { HubConnectionState };
