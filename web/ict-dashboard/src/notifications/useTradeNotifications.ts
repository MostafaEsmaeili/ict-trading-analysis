// ---------------------------------------------------------------------------------------------------
// useTradeNotifications — turns paper-trade lifecycle TRANSITIONS into closable toasts:
//   - a trade id we've not seen Open before → notifyTradeOpened
//   - a trade we'd seen Open that flips to Closed → notifyTradeClosed (win/loss tone from realizedR)
//
// Two feeds drive it, sharing ONE per-id seen-status ref so neither double-fires:
//   1. The live SignalR TradeUpdated push (the primary source for CLOSES — the active-trades cache drops
//      a closed trade, so the raw push is the only place a close is visible). The returned `onTradeEvent`
//      is wired into useTradingHub.
//   2. The polled active-trades snapshot (catches OPENS in mocks mode / when the socket is down). The
//      FIRST resolved snapshot only SEEDS the map (no toast) so a page load doesn't toast every
//      already-open trade.
//
// Each notice carries a deterministic id (triggers.ts) so the two feeds + the 30s poll de-dupe.
// ---------------------------------------------------------------------------------------------------

import { useCallback, useEffect, useRef } from 'react';
import { useActiveTrades } from '../api/hooks';
import { notifyTradeClosed, notifyTradeOpened } from './triggers';
import type { PaperTradeDto } from '../types/api';

type SeenStatus = 'Open' | 'Closed';

function normalize(t: PaperTradeDto): SeenStatus {
  return t.status === 'Closed' || t.lifecycle === 'Closed' ? 'Closed' : 'Open';
}

export interface UseTradeNotificationsResult {
  /** Feed a raw trade push here (wired to useTradingHub.onTradeEvent) — fires opened/closed transitions. */
  onTradeEvent: (trade: PaperTradeDto) => void;
}

export function useTradeNotifications(): UseTradeNotificationsResult {
  const tradesQ = useActiveTrades();
  // Map<tradeId, last-seen status>. Survives renders; never triggers a re-render.
  const seen = useRef<Map<string, SeenStatus>>(new Map());
  // Don't notify on the very first resolved snapshot (seed only) — those trades pre-date this session.
  const seeded = useRef(false);

  const handle = useCallback((t: PaperTradeDto) => {
    const status = normalize(t);
    const prev = seen.current.get(t.id);
    if (prev === undefined && status === 'Open') {
      notifyTradeOpened(t);
    } else if (prev !== 'Closed' && status === 'Closed') {
      notifyTradeClosed(t);
    }
    seen.current.set(t.id, status);
  }, []);

  // Feed 2 — the polled snapshot. Seeds silently on first resolve, then fires opens it sees appear.
  useEffect(() => {
    const trades = tradesQ.data;
    if (!trades) return;

    if (!seeded.current) {
      for (const t of trades) seen.current.set(t.id, normalize(t));
      seeded.current = true;
      return;
    }
    for (const t of trades) handle(t);
  }, [tradesQ.data, handle]);

  return { onTradeEvent: handle };
}
