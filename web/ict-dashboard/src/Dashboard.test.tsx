// Dashboard render test — the three panels (Alerts, Chart, Active Paper Trades + Performance) render and
// the mock-backed data appears. Proves the React Query wiring resolves the fixtures and that the
// read-only "Advisory · Paper only" posture badge is present (the defensive guardrail at the UI — §6.3).
import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { lightweightChartsMock } from './test/lwcMock';

vi.mock('lightweight-charts', () => lightweightChartsMock());

import { App } from './App';

describe('Dashboard', () => {
  it('renders the three panels and the advisory posture badge', async () => {
    render(<App />);

    // Panels by their accessible names.
    expect(screen.getByRole('region', { name: /alerts feed/i })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: /ict pattern chart/i })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: /active paper trades/i })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: /performance/i })).toBeInTheDocument();

    // Defensive guardrail surfaced in the UI — there is no execute/order control anywhere.
    expect(screen.getByText(/advisory · paper only/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /execute|go live|place order|buy|sell/i })).toBeNull();

    // Mock data resolves through React Query into the panels.
    await waitFor(() => {
      expect(screen.getAllByText('EURUSD').length).toBeGreaterThan(0);
    });
    // Style filter exposes the four frozen styles.
    expect(screen.getByRole('group', { name: /trade style filter/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Intraday' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('exposes the overlay legend toggles for every §9.1 concept', () => {
    render(<App />);
    const legend = screen.getByRole('group', { name: /overlay toggles/i });
    expect(legend).toBeInTheDocument();
    // Spot-check a few of the §9.1 overlay kinds.
    expect(screen.getByRole('button', { name: /fair value gap/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /ote 62/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /killzone/i })).toBeInTheDocument();
  });
});
