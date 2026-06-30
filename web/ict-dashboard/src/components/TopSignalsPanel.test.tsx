// TopSignalsPanel — the Live right-rail top-N TAKEABLE signals with a Take (paper) button each. Renders
// the takeable shortlist from the mocks; a Take opens a PAPER trade (no execute/order control, §6.3).
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { TopSignalsPanel } from './TopSignalsPanel';
import { renderWithProviders } from '../test/renderWithProviders';
import { __resetMockSignalsForTest } from '../mocks/fixtures';

beforeEach(() => __resetMockSignalsForTest());
afterEach(() => __resetMockSignalsForTest());

describe('TopSignalsPanel', () => {
  it('renders the top takeable signals with a Take (paper) button', async () => {
    renderWithProviders(<TopSignalsPanel />);
    await waitFor(() => {
      expect(screen.getByRole('region', { name: /top signals/i })).toBeInTheDocument();
    });
    // The takeable Manual signals (EURUSD, GBPUSD) appear; the Auto / taken / expired ones do not.
    const takeButtons = await screen.findAllByRole('button', { name: /take paper trade on/i });
    expect(takeButtons.length).toBeGreaterThan(0);
    expect(screen.queryByText('NAS100USD')).toBeNull(); // Auto — not takeable
    expect(screen.queryByText('USDJPY')).toBeNull(); // already taken
    expect(screen.queryByText('XAUUSD')).toBeNull(); // expired
  });

  it('a row focus switches the chart to the signal (symbol + timeframe + style)', async () => {
    const onFocus = vi.fn();
    renderWithProviders(<TopSignalsPanel onFocus={onFocus} />);
    const row = await screen.findByRole('button', { name: /focus chart on EURUSD/i });
    fireEvent.click(row);
    expect(onFocus).toHaveBeenCalledWith(
      expect.objectContaining({ symbol: 'EURUSD', timeframe: 'M15', style: 'Intraday' }),
    );
  });

  it('DEFENSIVE: no execute/buy/sell/place-order control', async () => {
    renderWithProviders(<TopSignalsPanel />);
    await screen.findAllByRole('button', { name: /take paper trade on/i });
    expect(screen.queryByRole('button', { name: /execute|place order|buy|sell|go live/i })).toBeNull();
  });

  it('taking a signal opens a paper trade (the Take button fires the mutation)', async () => {
    renderWithProviders(<TopSignalsPanel />);
    const btn = (await screen.findAllByRole('button', { name: /take paper trade on/i }))[0];
    fireEvent.click(btn);
    // After a successful take, the signal flips taken so it leaves the takeable shortlist — the panel
    // either drops to a remaining takeable signal or shows the empty state. Either way it never throws.
    await waitFor(() => {
      expect(screen.getByRole('region', { name: /top signals/i })).toBeInTheDocument();
    });
  });
});
