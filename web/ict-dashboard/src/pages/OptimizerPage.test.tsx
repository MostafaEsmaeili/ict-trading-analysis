// OptimizerPage — the parameter-grid optimizer (plan §15 §6). Running the grid renders a ranked
// leaderboard (top row highlighted, score descending). Read-only: only ranks simulated backtests.
import { describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { OptimizerPage } from './OptimizerPage';
import { renderWithProviders } from '../test/renderWithProviders';

describe('OptimizerPage', () => {
  it('runs the grid and renders a ranked leaderboard, ordered by score descending', async () => {
    renderWithProviders(<OptimizerPage />);

    // Datasets populate the symbol multi-select toggles (wait for a symbol chip to appear).
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'EURUSD' })).toBeInTheDocument();
    });

    // Submit the form (clicking the submit button does not reliably fire submit under jsdom/RTL here).
    fireEvent.submit(screen.getByRole('form', { name: /optimizer form/i }));

    const board = await screen.findByRole('table', { name: /leaderboard/i });
    // The leaderboard rows are focusable (role="button" for drill-in), so query the tbody <tr> directly.
    const bodyRows = Array.from(board.querySelectorAll('tbody tr'));
    expect(bodyRows.length).toBeGreaterThan(0);

    // The first body row is flagged as the top (rank 1) — the rank is the first cell.
    expect(bodyRows[0].className).toContain('row--top');
    expect(bodyRows[0].querySelector('td')?.textContent).toBe('1');

    // Scores are non-increasing down the board (ranked by the objective score).
    const scores = bodyRows.map((r) => {
      const cells = r.querySelectorAll('td');
      return Number(cells[cells.length - 1].textContent);
    });
    for (let i = 1; i < scores.length; i += 1) {
      expect(scores[i]).toBeLessThanOrEqual(scores[i - 1]);
    }
  });

  it('shows the empty prompt before a run', async () => {
    renderWithProviders(<OptimizerPage />);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /optimize/i })).toBeInTheDocument();
    });
    expect(screen.getByText(/configure a grid/i)).toBeInTheDocument();
  });

  it('has no execute/order control (read-only §6.3)', async () => {
    renderWithProviders(<OptimizerPage />);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /optimize/i })).toBeInTheDocument();
    });
    expect(
      screen.queryByRole('button', { name: /execute|place order|buy|sell|go live/i }),
    ).toBeNull();
  });
});
