// TradesPage — the full trades history (plan §15 §4). Loads the mock all-trades fixture, renders the
// sortable table + a filter bar, and applies the status filter. Read-only.
import { describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { TradesPage } from './TradesPage';
import { renderWithProviders } from '../test/renderWithProviders';
import { MOCK_ALL_TRADES } from '../mocks/fixtures';

/** Count the data rows in the trades grid. The page makes each <tr> focusable (role="button"), so the
 *  data rows are NOT role="row" — count the tbody <tr> elements directly. */
function bodyRowCount(): number {
  const table = screen.getByRole('table', { name: /trades/i });
  return table.querySelector('tbody')?.querySelectorAll('tr').length ?? 0;
}

describe('TradesPage', () => {
  it('renders the full trades table from the mock history', async () => {
    renderWithProviders(<TradesPage />);
    // Wait for the query to settle into the grid: one body row per trade.
    await waitFor(() => {
      expect(bodyRowCount()).toBe(MOCK_ALL_TRADES.length);
    });
  });

  it('filters to Closed trades only when the status filter is applied', async () => {
    renderWithProviders(<TradesPage />);
    await waitFor(() => {
      expect(bodyRowCount()).toBe(MOCK_ALL_TRADES.length);
    });

    fireEvent.change(screen.getByLabelText(/status filter/i), { target: { value: 'Closed' } });

    const closedCount = MOCK_ALL_TRADES.filter((t) => t.status === 'Closed').length;
    await waitFor(() => {
      expect(bodyRowCount()).toBe(closedCount);
    });
    // No "Open" status pill remains (the option labels still contain "Open"; assert on the pill).
    expect(screen.queryByText('Open', { selector: '.pill' })).toBeNull();
  });

  it('exposes the filter controls (status / symbol / style / result)', async () => {
    renderWithProviders(<TradesPage />);
    await screen.findByRole('table', { name: /trades/i });
    expect(screen.getByLabelText(/status filter/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/symbol filter/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/style filter/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/win\/loss filter/i)).toBeInTheDocument();
  });
});
