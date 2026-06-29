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

    // Defensive guardrail surfaced in the UI — there is no execute/order control anywhere, even with the
    // new notification controls (bell + health dot + closable toasts) present.
    expect(screen.getByText(/advisory · paper only/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /execute|go live|place order|buy|sell/i })).toBeNull();
    expect(screen.queryByRole('link', { name: /execute|go live|place order|buy|sell/i })).toBeNull();

    // The notification controls are present in the NavBar (the operator's #1 complaint fix).
    expect(screen.getByRole('button', { name: /notifications/i })).toBeInTheDocument();
    // The system-health dot is bound to backend state, NOT to any order/execute action.
    expect(screen.getByRole('status', { name: /healthy|degraded|backend error/i })).toBeInTheDocument();

    // The 6-page nav is present on every page (§15) — Signals lands after Live.
    const nav = screen.getByRole('navigation', { name: /primary/i });
    expect(nav).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Live' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Signals' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Trades' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Backtest' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Optimizer' })).toBeInTheDocument();

    // The Top Signals panel is present in the Live right rail (the manual-take shortcut, §15).
    expect(screen.getByRole('region', { name: /top signals/i })).toBeInTheDocument();

    // Mock data resolves through React Query into the panels.
    await waitFor(() => {
      expect(screen.getAllByText('EURUSD').length).toBeGreaterThan(0);
    });

    // The Take control opens a PAPER trade — its label must avoid every forbidden verb (the guardrail
    // holds even with the new take workflow on the Live page).
    await waitFor(() => {
      expect(screen.getAllByRole('button', { name: /take paper trade on/i }).length).toBeGreaterThan(0);
    });
    expect(
      screen.queryByRole('button', { name: /execute|go live|place order|buy|sell/i }),
    ).toBeNull();
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
