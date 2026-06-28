// MarketStatus — the Live-page NY-session clock widget (plan §9). Shows OPEN/CLOSED, the current
// session (highlighted when it's an active killzone), and the next session + a live countdown
// ("Xh Ym"). Read-only: a status feed never places an order (§6.3).
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MarketStatus } from './MarketStatus';
import { MOCK_MARKET_STATUS } from '../mocks/fixtures';

describe('MarketStatus', () => {
  // Pin the wall clock so the countdown interpolation (now - fetchedAt) is deterministic.
  const FIXED_NOW = Date.parse('2026-06-22T13:30:00Z');

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(FIXED_NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders an OPEN indicator and the current NY session', () => {
    render(<MarketStatus status={MOCK_MARKET_STATUS} fetchedAt={FIXED_NOW} isLoading={false} />);

    expect(screen.getByText('OPEN')).toBeInTheDocument();
    expect(screen.getByText('09:30:00 NY')).toBeInTheDocument();
    // The current session is NewYorkOpen, shown via the killzone badge (also appears in the hunt-set).
    expect(screen.getAllByText('NewYorkOpen').length).toBeGreaterThan(0);
  });

  it('formats the next-session countdown as "Xh Ym" (540 min → 9h 00m)', () => {
    render(<MarketStatus status={MOCK_MARKET_STATUS} fetchedAt={FIXED_NOW} isLoading={false} />);
    // 540 minutes, no wall-clock elapsed since the (fixed) fetch instant → exactly 9h 00m.
    expect(screen.getByText('9h 00m')).toBeInTheDocument();
    expect(screen.getByText(/opens in/i)).toBeInTheDocument();
  });

  it('renders a CLOSED indicator when the market is shut', () => {
    render(
      <MarketStatus
        status={{
          ...MOCK_MARKET_STATUS,
          marketOpen: false,
          currentSession: 'None',
          inActiveKillzone: false,
        }}
        fetchedAt={FIXED_NOW}
        isLoading={false}
      />,
    );
    expect(screen.getByText('CLOSED')).toBeInTheDocument();
    expect(screen.getByText(/no active session/i)).toBeInTheDocument();
  });

  it('shows a visible error state when the market-status query fails (§6.3)', () => {
    render(
      <MarketStatus
        status={undefined}
        fetchedAt={undefined}
        isLoading={false}
        isError
        error={new Error('market-status host error')}
      />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent(/market-status host error/i);
  });

  it('has no order/execute control (read-only)', () => {
    render(<MarketStatus status={MOCK_MARKET_STATUS} fetchedAt={FIXED_NOW} isLoading={false} />);
    expect(
      screen.queryByRole('button', { name: /buy|sell|execute|trade|order/i }),
    ).toBeNull();
  });
});
