// useSystemHealth — the green/amber/red signal bound to the core Live query error state + the SignalR
// hub state. CRITICAL guardrail check: dismissing a toast must NOT change health (the dot is the
// persistent backend truth; the toast is just a transient convenience). The core query hooks are mocked
// so we control the error state without a live host.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';

// Mutable mock state for the five watched queries.
const state = {
  alerts: false,
  trades: false,
  performance: false,
  marketStatus: false,
  account: false,
};

vi.mock('../api/hooks', () => ({
  useAlerts: () => ({ isError: state.alerts, error: state.alerts ? new Error('alerts down') : null }),
  useActiveTrades: () => ({ isError: state.trades, error: null }),
  usePerformance: () => ({ isError: state.performance, error: null }),
  useMarketStatus: () => ({ isError: state.marketStatus, error: null }),
  useAccountStatus: () => ({ isError: state.account, error: null }),
}));

import { useSystemHealth } from './useSystemHealth';
import { __resetHubHealthForTest } from './hubHealthStore';
import { __resetNotificationsForTest, notify, dismiss, getSnapshot } from './notificationStore';

beforeEach(() => {
  state.alerts = false;
  state.trades = false;
  state.performance = false;
  state.marketStatus = false;
  state.account = false;
  __resetHubHealthForTest();
  __resetNotificationsForTest();
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('useSystemHealth', () => {
  it('is green when every core query is healthy and the hub is connected', () => {
    const { result } = renderHook(() => useSystemHealth({ hub: 'connected' }));
    expect(result.current.status).toBe('green');
    expect(result.current.failing).toEqual([]);
  });

  it('is amber when the hub is reconnecting but the data still polls fine', () => {
    const { result } = renderHook(() => useSystemHealth({ hub: 'connecting' }));
    expect(result.current.status).toBe('amber');
  });

  it('goes RED and lists the failing endpoint when a core query errors', () => {
    state.alerts = true;
    const { result } = renderHook(() => useSystemHealth({ hub: 'connected' }));
    expect(result.current.status).toBe('red');
    expect(result.current.failing).toContain('Alerts');
  });

  it('dismissing a toast does NOT change health (health is decoupled from the toast store)', () => {
    state.alerts = true;
    const { result } = renderHook(() => useSystemHealth({ hub: 'connected' }));
    expect(result.current.status).toBe('red');

    // Raise + then dismiss an error toast — the still-failing query must keep health RED.
    let id = '';
    act(() => {
      id = notify({ kind: 'error', title: 'Alerts unavailable' });
    });
    act(() => {
      dismiss(id);
    });
    // The toast is gone from the active stack…
    expect(getSnapshot()[0].read).toBe(true);
    // …but health is unchanged: the backend is still down.
    expect(result.current.status).toBe('red');
    expect(result.current.failing).toContain('Alerts');
  });
});
