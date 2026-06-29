// ---------------------------------------------------------------------------------------------------
// useSystemHealth — a green/amber/red health signal bound to the core Live React-Query error state +
// the SignalR connection state. It deliberately does NOT read the notification store: dismissing a
// toast must NEVER hide a still-failing backend (the operator's explicit requirement — a closable
// notice is for transient events, the health dot is the persistent truth, §6.3).
//
//   green  — every core query is healthy AND (in a live build) the hub is connected.
//   amber  — the hub is reconnecting/disconnected but the data still polls fine (degraded live push).
//   red    — one or more core queries are erroring (the host/DB is down for that read).
//
// The hook reads the live React-Query observers directly (the same hooks the panels use), so it reflects
// the real fetch state, not a copy. The hub state is passed in (the hub lifecycle lives in useDashboardData).
// ---------------------------------------------------------------------------------------------------

import { useMemo, useSyncExternalStore } from 'react';
import {
  useAccountStatus,
  useActiveTrades,
  useAlerts,
  useMarketStatus,
  usePerformance,
} from '../api/hooks';
import { getHubHealth, subscribeHubHealth } from './hubHealthStore';

export type HealthStatus = 'green' | 'amber' | 'red';

/** A coarse view of the SignalR connection used by the health signal (decoupled from @microsoft/signalr). */
export type HubHealth = 'connected' | 'connecting' | 'disconnected' | 'disabled';

export interface SystemHealth {
  status: HealthStatus;
  /** Endpoint labels currently erroring (for the tooltip). Empty when all core reads are healthy. */
  failing: string[];
  hub: HubHealth;
}

/** Subscribe to the module-level hub-health store (published by useDashboardData). */
export function useHubHealth(): HubHealth {
  return useSyncExternalStore(subscribeHubHealth, getHubHealth, getHubHealth);
}

export interface UseSystemHealthArgs {
  /**
   * The SignalR hub connection health. When omitted the hook reads the module-level hubHealthStore (the
   * Live page publishes it there), so the global NavBar dot reflects the real socket state without
   * prop-drilling. In a mocks build the hub stays `'disabled'` and the dot reads healthy on data alone.
   */
  hub?: HubHealth;
}

/**
 * Bind the health dot to the core Live queries + the hub state. Each erroring query contributes its
 * label to `failing`; any error → red. Otherwise a degraded (connecting/disconnected) hub → amber while
 * the data still polls; all-healthy + connected/disabled → green.
 */
export function useSystemHealth(args: UseSystemHealthArgs = {}): SystemHealth {
  const storeHub = useHubHealth();
  const hub = args.hub ?? storeHub;
  const alertsQ = useAlerts();
  const tradesQ = useActiveTrades();
  const perfQ = usePerformance();
  const marketQ = useMarketStatus();
  const accountQ = useAccountStatus();

  return useMemo<SystemHealth>(() => {
    const failing: string[] = [];
    if (alertsQ.isError) failing.push('Alerts');
    if (tradesQ.isError) failing.push('Active trades');
    if (perfQ.isError) failing.push('Performance');
    if (marketQ.isError) failing.push('Market status');
    if (accountQ.isError) failing.push('Account');

    let status: HealthStatus;
    if (failing.length > 0) {
      status = 'red';
    } else if (hub === 'connecting' || hub === 'disconnected') {
      status = 'amber';
    } else {
      status = 'green';
    }
    return { status, failing, hub };
  }, [
    alertsQ.isError,
    tradesQ.isError,
    perfQ.isError,
    marketQ.isError,
    accountQ.isError,
    hub,
  ]);
}
