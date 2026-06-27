// ---------------------------------------------------------------------------------------------------
// Pure cache-merge helpers (plan §9 — "SignalR pushes deltas merged via setQueryData"). Kept pure so
// they unit-test without a QueryClient or a socket. Each prepends/upserts a delta into the cached list,
// newest-first, de-duplicated by id (or upsert by openTime for candles).
// ---------------------------------------------------------------------------------------------------

import type { AlertDto, CandleDto, PaperTradeDto, SetupDto } from '../types/api';
import type { ChartOverlay } from '../types/overlays';
import { setupToOverlays } from '../chart/setupToOverlays';

const MAX_FEED = 200;

/**
 * In-memory candle cap (mirrors the host's ChartCandleStore.MaxCandlesPerSeries). Bounds memory in live
 * mode so the series doesn't grow without limit as bars stream in over a long session.
 */
const MAX_CANDLES = 1500;

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

/**
 * Merge a live candle into the cached series, keeping it ASCENDING and UNIQUE by openTimeUtc and bounded
 * to MAX_CANDLES. lightweight-charts' setData requires strictly-ascending time, so a late/duplicate bar
 * (e.g. redelivered after a withAutomaticReconnect gap) must upsert in place / insert in order — never
 * blindly append (which would produce a non-monotonic series → a dev-mode throw or silent prod
 * corruption). openTimeUtc is an ISO-8601 UTC string, so lexicographic compare == chronological.
 */
export function appendCandle(list: CandleDto[] | undefined, candle: CandleDto): CandleDto[] {
  const base = list ?? [];

  // Hot path: a new bar strictly newer than the last → append (the common forming/closed-bar case).
  const last = base[base.length - 1];
  if (!last || candle.openTimeUtc > last.openTimeUtc) {
    return cap([...base, candle]);
  }

  // Same time as an existing bar → upsert in place (forming-bar update / redelivery).
  const at = base.findIndex((c) => c.openTimeUtc === candle.openTimeUtc);
  if (at >= 0) {
    const next = base.slice();
    next[at] = candle;
    return next;
  }

  // Earlier than the last bar and not present → insert at the ascending position so order is preserved.
  const insertAt = base.findIndex((c) => c.openTimeUtc > candle.openTimeUtc);
  return cap([...base.slice(0, insertAt), candle, ...base.slice(insertAt)]);
}

/** Trim the series to the newest MAX_CANDLES bars (the head, oldest, is dropped). */
function cap(list: CandleDto[]): CandleDto[] {
  return list.length > MAX_CANDLES ? list.slice(-MAX_CANDLES) : list;
}

/**
 * A newly detected setup contributes its derived overlay geometry to the chart's overlay cache.
 * De-duplicates by source setup id (newest wins): a redelivered deterministic setup id REPLACES its
 * prior overlays instead of stacking a second identical Entry/Stop/T1/Draw set — mirroring upsertById.
 */
export function mergeSetupOverlays(
  list: ChartOverlay[] | undefined,
  setup: SetupDto,
): ChartOverlay[] {
  const without = (list ?? []).filter((o) => !('setupId' in o) || o.setupId !== setup.id);
  return [...without, ...setupToOverlays(setup)];
}
