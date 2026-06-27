// TradesTable — the reusable trades grid (plan §15 §4). Renders a row per trade + a totals footer with
// the net-P&L sum and win rate; the close-reason pills carry their semantic class; sorting is stable.
// Read-only: there is NO execute/close/modify control on the grid (§6.3).
import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { TradesTable } from './TradesTable';
import { MOCK_CLOSED_TRADES, MOCK_ALL_TRADES } from '../mocks/fixtures';

describe('TradesTable', () => {
  it('renders a row per trade plus a totals footer row', () => {
    render(<TradesTable trades={MOCK_CLOSED_TRADES} />);
    const table = screen.getByRole('table', { name: /trades/i });

    // Header + N body rows + 1 totals row.
    const rows = within(table).getAllByRole('row');
    // thead(1) + body(MOCK_CLOSED_TRADES.length) + tfoot(1).
    expect(rows).toHaveLength(1 + MOCK_CLOSED_TRADES.length + 1);

    // The totals row reports the trade count and the win-rate.
    expect(screen.getByText(/trades · /i)).toBeInTheDocument();
    expect(screen.getByText(/closed · win/i)).toBeInTheDocument();
  });

  it('shows close-reason pills in their semantic colours', () => {
    render(<TradesTable trades={MOCK_CLOSED_TRADES} />);
    // The fixtures include TargetHit (win), StopHit (loss), TimeExit and Manual closes.
    expect(screen.getAllByText('TargetHit').length).toBeGreaterThan(0);
    expect(screen.getAllByText('StopHit').length).toBeGreaterThan(0);
    expect(screen.getByText('TimeExit')).toBeInTheDocument();
    expect(screen.getByText('Manual')).toBeInTheDocument();
    // A loss close-reason pill carries the semantic loss class.
    expect(screen.getAllByText('StopHit')[0]).toHaveClass('pill--loss');
    expect(screen.getAllByText('TargetHit')[0]).toHaveClass('pill--win');
  });

  it('renders the empty state when there are no trades', () => {
    render(<TradesTable trades={[]} emptyMessage="No trades match the filters." />);
    expect(screen.getByText('No trades match the filters.')).toBeInTheDocument();
  });

  it('surfaces an error state visibly (never a silent blank) — §6.3', () => {
    render(<TradesTable trades={[]} isError error={new Error('host down')} />);
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(/host down/i);
  });

  it('has NO execute/order/close control anywhere on the grid (read-only guardrail)', () => {
    render(<TradesTable trades={MOCK_ALL_TRADES} />);
    expect(
      screen.queryByRole('button', { name: /execute|close trade|place order|buy|sell|go live/i }),
    ).toBeNull();
  });
});
