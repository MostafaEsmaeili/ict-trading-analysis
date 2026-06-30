// ---------------------------------------------------------------------------------------------------
// hubHealthStore — a tiny module-level store for the SignalR connection health so the global NavBar
// SystemHealthIndicator can reflect it WITHOUT prop-drilling the hub through the AppLayout shell. The
// Live page's useDashboardData (which owns the hub lifecycle) publishes the state here; the NavBar dot
// subscribes. Mirrors the notification store's useSyncExternalStore pattern — no provider needed.
//
// This is SEPARATE from the notification store on purpose: the health signal must never be coupled to
// the toast/notice state (dismissing a toast can't change connection health).
// ---------------------------------------------------------------------------------------------------

import type { HubHealth } from './useSystemHealth';

let hubHealth: HubHealth = 'disabled';
const listeners = new Set<() => void>();

export function setHubHealth(next: HubHealth): void {
  if (next === hubHealth) return;
  hubHealth = next;
  for (const l of listeners) l();
}

export function getHubHealth(): HubHealth {
  return hubHealth;
}

export function subscribeHubHealth(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** TEST-ONLY: reset the hub-health singleton between tests. */
export function __resetHubHealthForTest(): void {
  hubHealth = 'disabled';
  for (const l of listeners) l();
}
