// notificationStore — the dependency-free module-level pub/sub behind the toasts + the center. Verifies
// notify/dismiss/clearAll, the history cap, the unread count, and the DEFENSIVE rule that error notices
// are sticky (autoDismissMs null) so a backend failure can never auto-vanish.
import { afterEach, describe, expect, it } from 'vitest';
import {
  HISTORY_CAP,
  __resetNotificationsForTest,
  activeToasts,
  clearAll,
  dismiss,
  getSnapshot,
  markAllRead,
  notify,
  unreadCount,
} from './notificationStore';

afterEach(() => {
  __resetNotificationsForTest();
});

describe('notificationStore', () => {
  it('notify adds a newest-first notice and exposes it in the snapshot', () => {
    notify({ kind: 'info', title: 'first' });
    notify({ kind: 'info', title: 'second' });
    const snap = getSnapshot();
    expect(snap.map((n) => n.title)).toEqual(['second', 'first']);
    expect(unreadCount()).toBe(2);
    expect(activeToasts()).toHaveLength(2);
  });

  it('dismiss marks a notice read (out of the toast stack) but keeps it in history', () => {
    const id = notify({ kind: 'tradeOpened', title: 'EURUSD opened' });
    notify({ kind: 'info', title: 'other' });
    dismiss(id);
    // Still in history…
    expect(getSnapshot()).toHaveLength(2);
    // …but no longer an active toast and not counted unread.
    expect(activeToasts().map((n) => n.title)).toEqual(['other']);
    expect(unreadCount()).toBe(1);
  });

  it('markAllRead clears the unread badge + toast stack without losing history', () => {
    notify({ kind: 'info', title: 'a' });
    notify({ kind: 'info', title: 'b' });
    markAllRead();
    expect(unreadCount()).toBe(0);
    expect(activeToasts()).toHaveLength(0);
    expect(getSnapshot()).toHaveLength(2);
  });

  it('clearAll drops the entire history', () => {
    notify({ kind: 'info', title: 'a' });
    notify({ kind: 'error', title: 'boom' });
    clearAll();
    expect(getSnapshot()).toHaveLength(0);
    expect(unreadCount()).toBe(0);
  });

  it('errors are STICKY (autoDismissMs null) even when a delay is requested', () => {
    notify({ kind: 'error', title: 'host down', autoDismissMs: 3000 });
    expect(getSnapshot()[0].autoDismissMs).toBeNull();
  });

  it('info/success notices get a finite default auto-dismiss', () => {
    notify({ kind: 'info', title: 'hi' });
    expect(getSnapshot()[0].autoDismissMs).toBeTypeOf('number');
    expect(getSnapshot()[0].autoDismissMs).toBeGreaterThan(0);
  });

  it('caps history at HISTORY_CAP, dropping the oldest', () => {
    for (let i = 0; i < HISTORY_CAP + 10; i += 1) {
      notify({ kind: 'info', title: `n${i}` });
    }
    const snap = getSnapshot();
    expect(snap).toHaveLength(HISTORY_CAP);
    // Newest is at the front; the oldest 10 were dropped.
    expect(snap[0].title).toBe(`n${HISTORY_CAP + 9}`);
    expect(snap.some((n) => n.title === 'n0')).toBe(false);
  });

  it('an explicit duplicate id is a no-op de-dupe', () => {
    notify({ id: 'trade-open-x', kind: 'tradeOpened', title: 'first' });
    notify({ id: 'trade-open-x', kind: 'tradeOpened', title: 'again' });
    expect(getSnapshot()).toHaveLength(1);
    expect(getSnapshot()[0].title).toBe('first');
  });
});
