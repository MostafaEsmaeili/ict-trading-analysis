// ToastViewport — renders the active toasts, the close button dismisses, and an error toast has NO
// auto-dismiss (it stays until explicitly closed — the defensive signal).
import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import { ToastViewport } from './ToastViewport';
import { __resetNotificationsForTest, getSnapshot, notify } from './notificationStore';

afterEach(() => {
  __resetNotificationsForTest();
  vi.useRealTimers();
});

describe('ToastViewport', () => {
  it('renders a toast for each active notice', () => {
    renderWithProviders(<ToastViewport />);
    act(() => {
      notify({ kind: 'tradeOpened', title: 'EURUSD Long opened' });
      notify({ kind: 'info', title: 'Heads up' });
    });
    expect(screen.getByText('EURUSD Long opened')).toBeInTheDocument();
    expect(screen.getByText('Heads up')).toBeInTheDocument();
  });

  it('the close button dismisses the toast', () => {
    renderWithProviders(<ToastViewport />);
    act(() => {
      notify({ kind: 'tradeClosed', title: 'EURUSD closed — win' });
    });
    expect(screen.getByText('EURUSD closed — win')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /dismiss notification/i }));
    expect(screen.queryByText('EURUSD closed — win')).toBeNull();
    // It stays in the durable history (just marked read).
    expect(getSnapshot()).toHaveLength(1);
    expect(getSnapshot()[0].read).toBe(true);
  });

  it('an error toast does NOT auto-dismiss (sticky) even after its kind default would elapse', () => {
    vi.useFakeTimers();
    renderWithProviders(<ToastViewport />);
    act(() => {
      notify({ kind: 'error', title: 'Alerts unavailable' });
    });
    expect(screen.getByText('Alerts unavailable')).toBeInTheDocument();
    // Advance well past any non-error default — the error toast must remain.
    act(() => {
      vi.advanceTimersByTime(60_000);
    });
    expect(screen.getByText('Alerts unavailable')).toBeInTheDocument();
  });

  it('a non-error toast auto-dismisses after its delay', () => {
    vi.useFakeTimers();
    renderWithProviders(<ToastViewport />);
    act(() => {
      notify({ kind: 'info', title: 'Auto goes away', autoDismissMs: 1000 });
    });
    expect(screen.getByText('Auto goes away')).toBeInTheDocument();
    // The timeout fires → dismiss() emits → React re-renders synchronously inside act().
    act(() => {
      vi.advanceTimersByTime(1500);
    });
    expect(screen.queryByText('Auto goes away')).toBeNull();
  });
});
