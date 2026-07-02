// SignalsFeed — the ranked, advisory signals list + the manual Take (paper) workflow. Asserts: ranked
// order, the Auto/Manual indicator, Take calls the mutation with the setupId, a blocked/taken/Auto signal
// is NOT takeable, and the defensive guardrail (NO execute/buy/sell/place-order control; the Take label
// avoids the forbidden verbs).
import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { SignalsFeed } from './SignalsFeed';
import type { RankedSignalDto, SetupDto } from '../types/api';

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
    model: 'Ict2022',
  };
}

const SIGNALS: RankedSignalDto[] = [
  { rank: 1, score: 84, entryMode: 'Manual', isTaken: false, blockReason: null, expiresAtUtc: null, setup: setup('s1', 'EURUSD', 'A', 84) },
  { rank: 2, score: 71, entryMode: 'Auto', isTaken: false, blockReason: null, expiresAtUtc: null, setup: setup('s2', 'NAS100USD', 'B', 71) },
  { rank: 3, score: 66, entryMode: 'Manual', isTaken: true, blockReason: 'AlreadyTaken', expiresAtUtc: null, setup: setup('s3', 'GBPUSD', 'B', 66) },
  { rank: 4, score: 63, entryMode: 'Manual', isTaken: false, blockReason: 'Expired', expiresAtUtc: null, setup: setup('s4', 'XAUUSD', 'B', 63) },
];

describe('SignalsFeed', () => {
  it('renders the signals in ranked order', () => {
    render(<SignalsFeed signals={SIGNALS} isLoading={false} onTake={vi.fn()} />);
    const ranks = screen.getAllByText(/^#\d/).map((el) => el.textContent);
    expect(ranks).toEqual(['#1', '#2', '#3', '#4']);
  });

  it('shows an Auto/Manual indicator per signal', () => {
    render(<SignalsFeed signals={SIGNALS} isLoading={false} onTake={vi.fn()} />);
    // The Manual signal exposes a Take button; the Auto signal exposes the "Auto — opens itself" chip.
    expect(screen.getByText('Auto — opens itself')).toBeInTheDocument();
    // The Manual chip appears for the manual rows.
    expect(screen.getAllByText('Manual').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Auto').length).toBeGreaterThan(0);
  });

  it('a takeable signal shows a Take (paper) button that calls onTake with the setupId', () => {
    const onTake = vi.fn();
    render(<SignalsFeed signals={[SIGNALS[0]]} isLoading={false} onTake={onTake} />);
    const btn = screen.getByRole('button', { name: /take paper trade on EURUSD/i });
    expect(btn).toHaveTextContent(/take \(paper\)/i);
    expect(btn).toBeEnabled();
    btn.click();
    expect(onTake).toHaveBeenCalledWith('s1');
  });

  it('an Auto signal shows no Take button (it opens itself)', () => {
    render(<SignalsFeed signals={[SIGNALS[1]]} isLoading={false} onTake={vi.fn()} />);
    expect(screen.queryByRole('button', { name: /take paper trade on/i })).toBeNull();
    expect(screen.getByText('Auto — opens itself')).toBeInTheDocument();
  });

  it('an already-taken signal disables the Take button with a reason', () => {
    render(<SignalsFeed signals={[SIGNALS[2]]} isLoading={false} onTake={vi.fn()} />);
    const btn = screen.getByRole('button', { name: /take paper trade on GBPUSD/i });
    expect(btn).toBeDisabled();
    expect(btn).toHaveAttribute('title', expect.stringMatching(/already taken/i));
  });

  it('an expired signal disables the Take button with a reason', () => {
    render(<SignalsFeed signals={[SIGNALS[3]]} isLoading={false} onTake={vi.fn()} />);
    const btn = screen.getByRole('button', { name: /take paper trade on XAUUSD/i });
    expect(btn).toBeDisabled();
    expect(btn).toHaveAttribute('title', expect.stringMatching(/expired/i));
  });

  it('shows Pending… on the row being taken', () => {
    render(<SignalsFeed signals={[SIGNALS[0]]} isLoading={false} onTake={vi.fn()} takingId="s1" />);
    const btn = screen.getByRole('button', { name: /take paper trade on EURUSD/i });
    expect(btn).toHaveTextContent(/pending/i);
    expect(btn).toBeDisabled();
  });

  it('DEFENSIVE: there is no execute/buy/sell/place-order control and the Take label avoids those verbs', () => {
    render(<SignalsFeed signals={SIGNALS} isLoading={false} onTake={vi.fn()} />);
    expect(screen.queryByRole('button', { name: /execute|go live|place order|buy|sell/i })).toBeNull();
    // Every take control reads "Take (paper)" — no forbidden verb leaks into the label.
    for (const btn of screen.getAllByRole('button', { name: /take paper trade on/i })) {
      expect(btn.textContent ?? '').not.toMatch(/execute|buy|sell|place order|go live/i);
    }
  });

  it('a row body click focuses the chart with the signal symbol + timeframe + style', () => {
    const onFocus = vi.fn();
    render(<SignalsFeed signals={[SIGNALS[0]]} isLoading={false} onTake={vi.fn()} onFocus={onFocus} />);
    const row = screen.getByRole('button', { name: /focus chart on EURUSD/i });
    row.click();
    expect(onFocus).toHaveBeenCalledWith({
      symbol: 'EURUSD',
      atUtc: '2026-06-22T06:00:00Z',
      timeframe: 'M15',
      style: 'Intraday',
    });
  });

  it('the Take button stops propagation (does not also focus the chart)', () => {
    const onFocus = vi.fn();
    const onTake = vi.fn();
    render(<SignalsFeed signals={[SIGNALS[0]]} isLoading={false} onTake={onTake} onFocus={onFocus} />);
    const action = screen.getByRole('button', { name: /take paper trade on EURUSD/i });
    action.click();
    expect(onTake).toHaveBeenCalledWith('s1');
    expect(onFocus).not.toHaveBeenCalled();
  });

  it('renders the score bar as a meter', () => {
    render(<SignalsFeed signals={[SIGNALS[0]]} isLoading={false} onTake={vi.fn()} />);
    const meter = within(screen.getByLabelText(/signals feed/i)).getByRole('meter');
    expect(meter).toHaveAttribute('aria-valuenow', '84');
  });

  it('shows the setup-model chip per row (multi-model support)', () => {
    const rows: RankedSignalDto[] = [
      { ...SIGNALS[0], setup: { ...SIGNALS[0].setup, model: 'Ict2022' } },
      { ...SIGNALS[1], setup: { ...SIGNALS[1].setup, model: 'Ict2024' } },
    ];
    render(<SignalsFeed signals={rows} isLoading={false} onTake={vi.fn()} />);
    // The short chip label is the year; the accessible title carries the full model name.
    const badge2022 = screen.getByText('2022');
    expect(badge2022).toHaveAttribute('title', 'Setup model: ICT 2022');
    const badge2024 = screen.getByText('2024');
    expect(badge2024).toHaveAttribute('title', 'Setup model: ICT 2024');
  });
});
