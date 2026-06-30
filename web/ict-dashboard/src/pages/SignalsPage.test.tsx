// SignalsPage — renders the ranked signals from the mocks, filters narrow the list, and a row focus
// deep-links to Live with the signal's symbol. Read-only/advisory: the Take control opens a PAPER trade
// only — there is no execute/order control (§6.3).
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { render } from '@testing-library/react';
import { SignalsPage } from './SignalsPage';
import { makeTestClient } from '../test/renderWithProviders';
import { __resetMockSignalsForTest } from '../mocks/fixtures';

/** A probe for the Live route — surfaces the router's current search string (the deep-link query). */
function LiveProbe() {
  const loc = useLocation();
  return <div data-testid="live-probe">{loc.search}</div>;
}

/** Render SignalsPage with a route table so a focus navigation to "/" lands on the probe. */
function renderSignals() {
  const client = makeTestClient();
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/signals']}>
        <Routes>
          <Route path="/signals" element={<SignalsPage />} />
          <Route path="/" element={<LiveProbe />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  __resetMockSignalsForTest();
});
afterEach(() => {
  __resetMockSignalsForTest();
});

/**
 * The SignalsFeed list region (aria-label "Signals feed"). Scoping the row queries here EXCLUDES the
 * WinnerSignalCard hero (region "Top signal") that also leads the page — the filter/feed assertions are
 * about the list, so they must not double-count the hero's #1 (which mirrors the top feed row).
 */
function feed(): HTMLElement {
  return screen.getByRole('region', { name: /signals feed/i });
}

/** A signal ROW is a "Focus chart on <symbol>" button — distinct from the symbol-filter <option>. */
function signalSymbols(): string[] {
  return within(feed())
    .queryAllByRole('button', { name: /focus chart on/i })
    .map((b) => b.getAttribute('aria-label')?.replace(/focus chart on /i, '').trim() ?? '');
}

describe('SignalsPage', () => {
  it('renders the ranked signals from the mocks', async () => {
    renderSignals();
    // Wait for the signal ROWS (not the filter <option>s) to render.
    await waitFor(() => expect(signalSymbols().length).toBeGreaterThan(0));
    // The matrix spans the five chart instruments (mixed grades).
    expect(signalSymbols()).toEqual(
      expect.arrayContaining(['EURUSD', 'NAS100USD', 'GBPUSD', 'USDJPY', 'XAUUSD']),
    );
    // A takeable Manual signal shows the Take (paper) button.
    expect(screen.getAllByRole('button', { name: /take paper trade on/i }).length).toBeGreaterThan(0);
  });

  it('the symbol filter narrows the list to one instrument', async () => {
    renderSignals();
    await waitFor(() => expect(signalSymbols()).toContain('NAS100USD'));

    fireEvent.change(screen.getByLabelText(/symbol filter/i), { target: { value: 'EURUSD' } });

    expect(signalSymbols()).toEqual(['EURUSD']);
  });

  it('the grade filter narrows to a single grade', async () => {
    renderSignals();
    await waitFor(() => expect(signalSymbols()).toContain('EURUSD'));

    // Only EURUSD is grade A in the fixture.
    fireEvent.change(screen.getByLabelText(/grade filter/i), { target: { value: 'A' } });
    expect(signalSymbols()).toEqual(['EURUSD']);
  });

  it('the min-RR filter drops lower-RR signals', async () => {
    renderSignals();
    await waitFor(() => expect(signalSymbols()).toContain('GBPUSD'));

    // GBPUSD RR is 3.1; EURUSD 2.6 — a 3.0 floor keeps GBPUSD but drops EURUSD.
    fireEvent.change(screen.getByLabelText(/minimum reward ratio/i), { target: { value: '3.0' } });
    expect(signalSymbols()).toContain('GBPUSD');
    expect(signalSymbols()).not.toContain('EURUSD');
  });

  it('hide-taken removes already-taken / blocked signals', async () => {
    renderSignals();
    await waitFor(() => expect(signalSymbols()).toContain('USDJPY')); // the taken one

    fireEvent.click(screen.getByLabelText(/hide taken signals/i));
    expect(signalSymbols()).not.toContain('USDJPY'); // already taken
    expect(signalSymbols()).not.toContain('XAUUSD'); // expired/blocked
    expect(signalSymbols()).toContain('EURUSD'); // still takeable
  });

  it('a row focus deep-links to Live with the signal symbol', async () => {
    renderSignals();
    await waitFor(() => expect(signalSymbols()).toContain('EURUSD'));

    fireEvent.click(within(feed()).getByRole('button', { name: /focus chart on EURUSD/i }));
    expect(await screen.findByTestId('live-probe')).toHaveTextContent('symbol=EURUSD');
  });

  it('has no execute/order control (read-only §6.3)', async () => {
    renderSignals();
    await waitFor(() => expect(signalSymbols().length).toBeGreaterThan(0));
    expect(screen.queryByRole('button', { name: /execute|place order|buy|sell|go live/i })).toBeNull();
  });
});
