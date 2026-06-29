// ---------------------------------------------------------------------------------------------------
// useErrorNotifications — raises a STICKY error toast when a core Live query fails, and clears it when
// the query recovers. The toast is a convenience surface; the persistent truth stays the SystemHealth
// dot (which does NOT read the toast store), so dismissing the toast never hides a still-down backend.
//
// Each endpoint keeps ONE error notice (deterministic `error-<key>` id de-dupes across the 30s poll).
// When a query flips back to healthy we dismiss its notice so a recovered endpoint clears its toast.
// ---------------------------------------------------------------------------------------------------

import { useEffect } from 'react';
import {
  useAccountStatus,
  useActiveTrades,
  useAlerts,
  useMarketStatus,
  usePerformance,
} from '../api/hooks';
import { errorMessage } from '../format-error';
import { dismiss } from './notificationStore';
import { notifyQueryError } from './triggers';

interface Watched {
  key: string;
  label: string;
  isError: boolean;
  error: unknown;
}

export function useErrorNotifications(): void {
  const alertsQ = useAlerts();
  const tradesQ = useActiveTrades();
  const perfQ = usePerformance();
  const marketQ = useMarketStatus();
  const accountQ = useAccountStatus();

  const watched: Watched[] = [
    { key: 'alerts', label: 'Alerts', isError: alertsQ.isError, error: alertsQ.error },
    { key: 'trades', label: 'Active trades', isError: tradesQ.isError, error: tradesQ.error },
    { key: 'performance', label: 'Performance', isError: perfQ.isError, error: perfQ.error },
    { key: 'market-status', label: 'Market status', isError: marketQ.isError, error: marketQ.error },
    { key: 'account', label: 'Account', isError: accountQ.isError, error: accountQ.error },
  ];

  useEffect(() => {
    for (const w of watched) {
      const noticeId = `error-${w.key}`;
      if (w.isError) {
        notifyQueryError(w.label, errorMessage(w.error), w.key);
      } else {
        // Recovered → clear its sticky error notice.
        dismiss(noticeId);
      }
    }
    // The watched array is rebuilt each render; depend on the raw error flags so we react to changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    alertsQ.isError,
    tradesQ.isError,
    perfQ.isError,
    marketQ.isError,
    accountQ.isError,
  ]);
}
