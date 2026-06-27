// BacktestPage — the Backtest Lab (plan §15 §5). Submitting the form runs POST /api/backtest (mock
// client in test mode) and renders KPI tiles, the balance curve and the trades table. Read-only.
import { describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { BacktestPage } from './BacktestPage';
import { renderWithProviders } from '../test/renderWithProviders';

describe('BacktestPage', () => {
  it('runs a backtest and renders KPI tiles + the trades table', async () => {
    renderWithProviders(<BacktestPage />);

    // The dataset-backed symbol selector populates from the mock datasets (wait for a real value, not
    // the empty pre-load state — submitting with an empty symbol is a no-op by design).
    await waitFor(() => {
      const sel = screen.getByLabelText(/backtest symbol/i) as HTMLSelectElement;
      expect(sel.value).not.toBe('');
    });

    // Submit the form (clicking the submit button does not reliably fire submit under jsdom/RTL here).
    fireEvent.submit(screen.getByRole('form', { name: /backtest form/i }));

    // Results: KPI tiles + the trades table appear once the (mock) mutation resolves.
    await waitFor(() => {
      expect(screen.getByTestId('kpi-tiles')).toBeInTheDocument();
    });
    expect(screen.getByTestId('balance-curve')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: /backtest trades/i })).toBeInTheDocument();

    // Ending balance KPI present.
    expect(screen.getByText(/ending balance/i)).toBeInTheDocument();
  });

  it('pre-fills the form from a deep-link (Optimizer drill-in)', async () => {
    renderWithProviders(<BacktestPage />, {
      initialEntries: ['/backtest?symbol=GBPUSD&timeframe=M5&style=Scalp&risk=1.5'],
      path: '/backtest',
    });

    await waitFor(() => {
      const symbolSel = screen.getByLabelText(/backtest symbol/i) as HTMLSelectElement;
      expect(symbolSel.value).toBe('GBPUSD');
    });
    const styleSel = screen.getByLabelText(/backtest style/i) as HTMLSelectElement;
    expect(styleSel.value).toBe('Scalp');
    const riskInput = screen.getByLabelText(/risk percent/i) as HTMLInputElement;
    expect(riskInput.value).toBe('1.5');
  });

  it('has no execute/order control on the page (read-only §6.3)', async () => {
    renderWithProviders(<BacktestPage />);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /run backtest/i })).toBeInTheDocument();
    });
    expect(
      screen.queryByRole('button', { name: /execute|place order|buy|sell|go live/i }),
    ).toBeNull();
  });
});
