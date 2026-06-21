// ---------------------------------------------------------------------------------------------------
// Pure cache-merge helpers (plan §9 — "SignalR pushes deltas merged via setQueryData"). Kept pure so
// they unit-test without a QueryClient or a socket. Each prepends/upserts a delta into the cached list,
// newest-first, de-duplicated by id (or upsert by openTime for candles).
// ---------------------------------------------------------------------------------------------------

import type { AlertDto, CandleDto, PaperTradeDto, SetupDto } from '../types/api';
import type { ChartOverlay } from '../types/overlays';
import { setupToOverlays } from '../chart/setupToOverlays';

const MAX_FEED = 200;

export function upsertById<T extends { id: string }>(list: T[] | undefined, next: T): T[] {
  const base = list ?? [];
  const without = base.filter((x) => x.id !== next.id);
  return [next, ...without].slice(0, MAX_FEED);
}

export function appendAlert(list: AlertDto[] | undefined, alert: AlertDto): AlertDto[] {
  return upsertById(list, alert);
}

export function upsertTrade(list: PaperTradeDto[] | undefined, trade: PaperTradeDto): PaperTradeDto[] {
  // A trade that has closed leaves the "active" list.
  const next = upsertById(list, trade);
  return next.filter((t) => t.status !== 'Closed');
}

export function appendCandle(list: CandleDto[] | undefined, candle: CandleDto): CandleDto[] {
  const base = list ?? [];
  const last = base[base.length - 1];
  if (last && last.openTimeUtc === candle.openTimeUtc) {
    // Same bar updated in place (live forming candle).
    return [...base.slice(0, -1), candle];
  }
  return [...base, candle];
}

/** A newly detected setup contributes its derived overlay geometry to the chart's overlay cache. */
export function mergeSetupOverlays(
  list: ChartOverlay[] | undefined,
  setup: SetupDto,
): ChartOverlay[] {
  return [...(list ?? []), ...setupToOverlays(setup)];
}
