// WinnerSignalCard — the HERO card featuring the #1 ranked signal (the single best opportunity right now).
// Asserts: it renders the rank-1 signal (symbol/grade/levels/RR/reason), Take (paper) fires the mutation
// with the rank-1 setupId, the calm empty state when there is no signal, a blocked/taken/Auto signal is
// NOT takeable, a card-body click focuses the chart, and the defensive guardrail — NO execute/buy/sell/
// place-order control, and the only action's label is "Take (paper)".
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import type { RankedSignalDto, SetupDto } from '../types/api';

// The card owns its data via useSignals + useTakeSignal — mock the hooks so each test controls the #1
// signal and observes the take mutation directly (no React Query / network setup needed).
const mutate = vi.fn();
const useSignals = vi.fn();
const useTakeSignal = vi.fn();

vi.mock('../api/hooks', () => ({
  useSignals: () => useSignals(),
  useTakeSignal: () => useTakeSignal(),
}));

import { WinnerSignalCard } from './WinnerSignalCard';

function setup(id: string, symbol: string, grade: SetupDto['grade'], score: number): SetupDto {
  return {
    id,
    symbol,
    direction: 'Bullish',
    killzone: 'LondonOpen',
    style: 'Intraday',
    grade,
    score,
    triggerTimeframe: 'M15',
    entry: 1.072,
    stop: 1.069,
    targets: [1.0762, 1.079],
    rewardRatio: 2.6,
    reason: 'Sweep → MSS → FVG → OTE → draw',
    detectedAtUtc: '2026-06-22T06:00:00Z',
    isAdvisoryOnly: true,
  };
}

function signal(over: Partial<RankedSignalDto> = {}): RankedSignalDto {
  return {
    rank: 1,
    score: 84,
    entryMode: 'Manual',
    isTaken: false,
    blockReason: null,
    expiresAtUtc: null,
    setup: setup('s1', 'EURUSD', 'A', 84),
    ...over,
  };
}

function mockSignals(data: RankedSignalDto[] | undefined, flags: Partial<{ isLoading: boolean; isError: boolean; error: unknown }> = {}) {
  useSignals.mockReturnValue({ data, isLoading: false, isError: false, error: null, ...flags });
}

function mockTake(over: Partial<{ isPending: boolean; isError: boolean; error: unknown; variables: { setupId: string } }> = {}) {
  useTakeSignal.mockReturnValue({ mutate, isPending: false, isError: false, error: null, variables: undefined, ...over });
}

beforeEach(() => {
  mutate.mockReset();
  mockTake();
});
afterEach(() => {
  vi.clearAllMocks();
});

describe('WinnerSignalCard', () => {
  it('renders the #1 signal — symbol, grade, score, levels, RR and the one-line reason', () => {
    mockSignals([signal()]);
    render(<WinnerSignalCard />);

    const card = screen.getByRole('region', { name: /top signal/i });
    expect(within(card).getByText('⭐')).toBeInTheDocument();
    expect(within(card).getByText('EURUSD')).toBeInTheDocument();
    expect(within(card).getByText('#1')).toBeInTheDocument();
    // The score bar renders the rank-1 score as a 0–100 meter.
    expect(within(card).getByRole('meter')).toHaveAttribute('aria-valuenow', '84');
    // Entry/stop/target prices + RR are present.
    expect(within(card).getByText('1.07200')).toBeInTheDocument(); // entry
    expect(within(card).getByText('1.06900')).toBeInTheDocument(); // stop
    expect(within(card).getByText(/1\.07620 \/ 1\.07900/)).toBeInTheDocument(); // targets
    expect(within(card).getByText(/RR 2\.6/)).toBeInTheDocument();
    expect(within(card).getByText(/Sweep → MSS → FVG → OTE → draw/)).toBeInTheDocument();
  });

  it('Take (paper) fires the take mutation with the rank-1 setupId', () => {
    mockSignals([signal()]);
    render(<WinnerSignalCard />);

    const btn = screen.getByRole('button', { name: /take paper trade on EURUSD/i });
    expect(btn).toHaveTextContent(/take \(paper\)/i);
    expect(btn).toBeEnabled();
    btn.click();
    expect(mutate).toHaveBeenCalledWith({ setupId: 's1' });
  });

  it('shows the calm empty state when there is no signal', () => {
    mockSignals([]);
    render(<WinnerSignalCard />);

    expect(
      screen.getByText(/no a\/b setup right now — waiting for a killzone/i),
    ).toBeInTheDocument();
    // The empty state is calm — NOT an error (no alert role text), and no Take button.
    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.queryByRole('button', { name: /take paper trade on/i })).toBeNull();
  });

  it('shows the empty state when the signals query has no data yet', () => {
    mockSignals(undefined);
    render(<WinnerSignalCard />);
    expect(
      screen.getByText(/no a\/b setup right now — waiting for a killzone/i),
    ).toBeInTheDocument();
  });

  it('a blocked (expired) #1 signal disables Take with a reason — no mutation fires', () => {
    mockSignals([signal({ blockReason: 'Expired' })]);
    render(<WinnerSignalCard />);

    const btn = screen.getByRole('button', { name: /take paper trade on EURUSD/i });
    expect(btn).toBeDisabled();
    expect(btn).toHaveAttribute('title', expect.stringMatching(/expired/i));
    btn.click();
    expect(mutate).not.toHaveBeenCalled();
  });

  it('an already-taken #1 signal disables Take with a reason', () => {
    mockSignals([signal({ isTaken: true, blockReason: 'AlreadyTaken' })]);
    render(<WinnerSignalCard />);

    const btn = screen.getByRole('button', { name: /take paper trade on EURUSD/i });
    expect(btn).toBeDisabled();
    expect(btn).toHaveAttribute('title', expect.stringMatching(/already taken/i));
  });

  it('an Auto #1 signal shows the self-opening chip and NO Take button', () => {
    mockSignals([signal({ entryMode: 'Auto' })]);
    render(<WinnerSignalCard />);

    expect(screen.getByText('Auto — opens itself')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /take paper trade on/i })).toBeNull();
  });

  it('shows Pending… while the #1 signal is being taken', () => {
    mockSignals([signal()]);
    mockTake({ isPending: true, variables: { setupId: 's1' } });
    render(<WinnerSignalCard />);

    const btn = screen.getByRole('button', { name: /take paper trade on EURUSD/i });
    expect(btn).toHaveTextContent(/pending/i);
    expect(btn).toBeDisabled();
  });

  it('clicking the card body focuses the chart on the signal (symbol + timeframe + style)', () => {
    const onFocus = vi.fn();
    mockSignals([signal()]);
    render(<WinnerSignalCard onFocus={onFocus} />);

    const body = screen.getByRole('button', { name: /focus chart on EURUSD/i });
    body.click();
    expect(onFocus).toHaveBeenCalledWith({
      symbol: 'EURUSD',
      atUtc: '2026-06-22T06:00:00Z',
      timeframe: 'M15',
      style: 'Intraday',
    });
  });

  it('the Take button stops propagation (does not also focus the chart)', () => {
    const onFocus = vi.fn();
    mockSignals([signal()]);
    render(<WinnerSignalCard onFocus={onFocus} />);

    screen.getByRole('button', { name: /take paper trade on EURUSD/i }).click();
    expect(mutate).toHaveBeenCalledWith({ setupId: 's1' });
    expect(onFocus).not.toHaveBeenCalled();
  });

  it('surfaces a take failure visibly above the action', () => {
    mockSignals([signal()]);
    mockTake({ isError: true, error: new Error('Signal already taken (409).') });
    render(<WinnerSignalCard />);
    expect(screen.getByRole('alert')).toHaveTextContent(/take failed — signal already taken/i);
  });

  it('surfaces a signals-query error visibly (not a silent blank)', () => {
    mockSignals(undefined, { isError: true, error: new Error('host error') });
    render(<WinnerSignalCard />);
    expect(screen.getByRole('alert')).toHaveTextContent(/top signal unavailable/i);
  });

  it('DEFENSIVE: there is no execute/buy/sell/place-order control; the only action is Take (paper)', () => {
    mockSignals([signal()]);
    render(<WinnerSignalCard />);

    expect(
      screen.queryByRole('button', { name: /execute|go live|place order|buy|sell/i }),
    ).toBeNull();
    const take = screen.getByRole('button', { name: /take paper trade on EURUSD/i });
    expect(take.textContent ?? '').not.toMatch(/execute|buy|sell|place order|go live/i);
  });
});
