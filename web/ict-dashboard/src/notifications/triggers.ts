// ---------------------------------------------------------------------------------------------------
// Notification triggers — the small adapters that turn dashboard events into notices. Kept separate
// from the store so the store stays a pure pub/sub and the trigger semantics are unit-testable.
//
// Wired NOW (data available today):
//   - notifyTradeOpened / notifyTradeClosed — from a TradeUpdated transition (useTradeNotifications).
//   - notifyQueryError — a core query/mutation failure → a STICKY error notice (the defensive signal).
//
// Reserved for a later slice (do NOT invent the signals types here): notifyNewOpportunity is the hook
// the future `SignalsUpdated` push will call when the host emits a discovery-mode opportunity. It is a
// thin pass-through to `notify` so the trigger site only needs symbol/title/body, not the store shape.
// ---------------------------------------------------------------------------------------------------

import { formatR } from '../format';
import { notify, type NoticeFocus } from './notificationStore';
import type { PaperTradeDto } from '../types/api';

/** A trade transitioned to Open (first time we've seen it open) → a `tradeOpened` notice. */
export function notifyTradeOpened(trade: PaperTradeDto): void {
  notify({
    // Deterministic id so a poll-reconcile re-emitting the same open is a no-op de-dupe.
    id: `trade-open-${trade.id}`,
    kind: 'tradeOpened',
    title: `${trade.symbol} ${trade.direction} opened`,
    body: `${trade.style} · entry ${trade.entry} · stop ${trade.stop}`,
    symbol: trade.symbol,
    focus: { symbol: trade.symbol, atUtc: trade.openedAtUtc },
  });
}

/** A trade transitioned to Closed → a `tradeClosed` notice, win/loss tone from the realized R. */
export function notifyTradeClosed(trade: PaperTradeDto): void {
  const r = trade.realizedR;
  const outcome = r == null ? 'closed' : r > 0 ? 'win' : r < 0 ? 'loss' : 'breakeven';
  notify({
    id: `trade-close-${trade.id}`,
    kind: 'tradeClosed',
    title: `${trade.symbol} ${trade.direction} closed — ${outcome}`,
    body: `${formatR(r)}${trade.closeReason ? ` · ${trade.closeReason}` : ''}`,
    symbol: trade.symbol,
    focus: { symbol: trade.symbol, atUtc: trade.closedAtUtc ?? trade.openedAtUtc },
  });
}

/**
 * A core query or a mutation failed → a STICKY error notice (never auto-dismisses). `key` de-dupes so a
 * repeatedly-failing poll doesn't spam the stack — the same endpoint keeps ONE error notice.
 */
export function notifyQueryError(label: string, message: string, key?: string): void {
  notify({
    id: key ? `error-${key}` : undefined,
    kind: 'error',
    title: `${label} unavailable`,
    body: message,
  });
}

/**
 * RESERVED for the future `SignalsUpdated` trigger (discovery-mode opportunities). A later slice will
 * wire the real signals push to this; the types for that feed don't exist yet, so this stays a thin,
 * explicit pass-through rather than inventing a contract here.
 */
export function notifyNewOpportunity(opportunity: {
  symbol: string;
  title: string;
  body?: string;
  atUtc?: string;
  focus?: NoticeFocus;
}): void {
  notify({
    kind: 'opportunity',
    title: opportunity.title,
    body: opportunity.body,
    symbol: opportunity.symbol,
    atUtc: opportunity.atUtc,
    focus: opportunity.focus ?? { symbol: opportunity.symbol, atUtc: opportunity.atUtc },
  });
}
