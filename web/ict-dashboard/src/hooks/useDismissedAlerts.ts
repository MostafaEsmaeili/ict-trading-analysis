// ---------------------------------------------------------------------------------------------------
// useDismissedAlerts — a client-side set of alert ids the operator has dismissed from the Alerts feed,
// persisted in localStorage so the dismissal survives a refresh (the feed is otherwise append-only and
// can only grow — part of the "notifications can't be closed" complaint). This is DISPLAY-ONLY: the
// underlying alert data on the host is untouched; we just hide dismissed rows locally.
//
// The dismissed-id set is bounded so it can't grow without limit over a long-lived session.
// ---------------------------------------------------------------------------------------------------

import { useCallback, useEffect, useState } from 'react';

const STORAGE_KEY = 'ict.dismissedAlerts.v1';
const MAX_DISMISSED = 500;

function read(): Set<string> {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return new Set();
    const parsed = JSON.parse(raw) as unknown;
    return Array.isArray(parsed) ? new Set(parsed.filter((x): x is string => typeof x === 'string')) : new Set();
  } catch {
    return new Set();
  }
}

function write(ids: Set<string>): void {
  try {
    // Keep only the most-recent MAX_DISMISSED ids (insertion order preserved by Set).
    const arr = [...ids].slice(-MAX_DISMISSED);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(arr));
  } catch {
    // localStorage may be unavailable (private mode / quota) — degrade to in-memory only.
  }
}

export interface UseDismissedAlertsResult {
  dismissed: Set<string>;
  isDismissed: (id: string) => boolean;
  dismiss: (id: string) => void;
  /** Dismiss every id in `ids` at once (the feed header "Clear all"). */
  dismissMany: (ids: string[]) => void;
}

export function useDismissedAlerts(): UseDismissedAlertsResult {
  const [dismissed, setDismissed] = useState<Set<string>>(() => read());

  // Persist on every change.
  useEffect(() => {
    write(dismissed);
  }, [dismissed]);

  const dismiss = useCallback((id: string) => {
    setDismissed((prev) => {
      if (prev.has(id)) return prev;
      const next = new Set(prev);
      next.add(id);
      return next;
    });
  }, []);

  const dismissMany = useCallback((ids: string[]) => {
    setDismissed((prev) => {
      const next = new Set(prev);
      let changed = false;
      for (const id of ids) {
        if (!next.has(id)) {
          next.add(id);
          changed = true;
        }
      }
      return changed ? next : prev;
    });
  }, []);

  const isDismissed = useCallback((id: string) => dismissed.has(id), [dismissed]);

  return { dismissed, isDismissed, dismiss, dismissMany };
}
