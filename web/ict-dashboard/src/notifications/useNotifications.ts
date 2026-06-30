// ---------------------------------------------------------------------------------------------------
// useNotifications — the React binding for the module-level notification store (notificationStore.ts).
// Uses `useSyncExternalStore` so every mounted consumer (ToastViewport + the NavBar NotificationCenter)
// re-renders from the SAME singleton the trigger sites push into — no Context/provider needed.
// ---------------------------------------------------------------------------------------------------

import { useSyncExternalStore } from 'react';
import {
  getSnapshot,
  subscribe,
  unreadCount as readUnreadCount,
  type Notice,
} from './notificationStore';

/** The full notice history (newest-first) — re-renders on every store change. */
export function useNotifications(): readonly Notice[] {
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}

/**
 * The ACTIVE (unread) toasts — derived from the same snapshot so the toast stack and the center stay in
 * sync. (Derived here rather than via a separate store getter to keep one source of truth + a stable
 * snapshot reference for the store contract.)
 */
export function useActiveToasts(): Notice[] {
  const all = useNotifications();
  return all.filter((n) => !n.read);
}

/** The unread count for the NavBar bell badge. */
export function useUnreadCount(): number {
  return useSyncExternalStore(subscribe, readUnreadCount, readUnreadCount);
}
