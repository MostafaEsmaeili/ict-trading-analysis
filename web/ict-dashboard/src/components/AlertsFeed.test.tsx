// AlertsFeed — the append-only advisory alerts list is now DISMISSIBLE (per-row ✕ + "Clear all"),
// persisted in localStorage so a tidied feed stays tidy across a refresh. The ✕ must NOT trigger the
// row's focus-chart click (stopPropagation), and the ERROR branch keeps NO dismiss control (a failed
// query is the defensive signal — it must stay visible, §6.3).
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { AlertsFeed } from './AlertsFeed';
import type { AlertDto } from '../types/api';

const ALERTS: AlertDto[] = [
  {
    id: 'a1',
    kind: 'SetupConfirmed',
    symbol: 'EURUSD',
    message: 'Sweep → MSS → FVG → OTE',
    direction: 'Bullish',
    killzone: 'LondonOpen',
    style: 'Intraday',
    atUtc: '2026-06-22T06:00:00Z',
  },
  {
    id: 'a2',
    kind: 'SetupConfirmed',
    symbol: 'GBPUSD',
    message: 'Order-block confluence',
    direction: 'Bearish',
    killzone: 'NewYorkOpen',
    style: 'Intraday',
    atUtc: '2026-06-22T13:30:00Z',
  },
];

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  localStorage.clear();
});

describe('AlertsFeed dismiss', () => {
  it('the per-row ✕ removes a row and persists the dismissal to localStorage', () => {
    const { unmount } = render(<AlertsFeed alerts={ALERTS} isLoading={false} />);
    expect(screen.getByText(/sweep → mss/i)).toBeInTheDocument();

    const closes = screen.getAllByRole('button', { name: /dismiss alert/i });
    fireEvent.click(closes[0]); // dismiss a1 (the first row)
    expect(screen.queryByText(/sweep → mss/i)).toBeNull();
    expect(screen.getByText(/order-block confluence/i)).toBeInTheDocument();

    // Persisted: re-mounting the feed keeps the dismissed row hidden.
    unmount();
    render(<AlertsFeed alerts={ALERTS} isLoading={false} />);
    expect(screen.queryByText(/sweep → mss/i)).toBeNull();
    expect(screen.getByText(/order-block confluence/i)).toBeInTheDocument();
  });

  it('dismissing does NOT fire the row focus-chart click (stopPropagation)', () => {
    const onFocus = vi.fn();
    render(<AlertsFeed alerts={ALERTS} isLoading={false} onFocus={onFocus} />);
    fireEvent.click(screen.getAllByRole('button', { name: /dismiss alert/i })[0]);
    expect(onFocus).not.toHaveBeenCalled();
  });

  it('"Clear all" dismisses every visible row → "All caught up." empty state', () => {
    render(<AlertsFeed alerts={ALERTS} isLoading={false} />);
    fireEvent.click(screen.getByRole('button', { name: /clear all/i }));
    expect(screen.getByText('All caught up.')).toBeInTheDocument();
  });

  it('the error branch has NO dismiss control (the defensive signal stays visible — §6.3)', () => {
    render(
      <AlertsFeed alerts={[]} isLoading={false} isError error={new Error('alerts host down')} />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent(/alerts host down/i);
    expect(screen.queryByRole('button', { name: /dismiss alert/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /clear all/i })).toBeNull();
  });

  it('still focuses the chart when the row (not the ✕) is clicked', () => {
    const onFocus = vi.fn();
    render(<AlertsFeed alerts={ALERTS} isLoading={false} onFocus={onFocus} />);
    fireEvent.click(screen.getByRole('button', { name: /focus chart on EURUSD/i }));
    expect(onFocus).toHaveBeenCalledWith({ symbol: 'EURUSD', atUtc: '2026-06-22T06:00:00Z' });
  });
});
