// SettingsPage — live per-instrument tuning + the read-only global concept view (plan §15). The
// per-instrument form is editable (k-of-n / required subset / per-pair costs); a required subset must
// include DisplacementMss (the FSM direction lock). Read-only/advisory: no order/execute control (§6.3).
import { describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { SettingsPage } from './SettingsPage';
import { renderWithProviders } from '../test/renderWithProviders';

describe('SettingsPage', () => {
  it('renders the per-instrument form and the current-overrides table (the baked NAS100 override)', async () => {
    renderWithProviders(<SettingsPage />);

    await waitFor(() => {
      expect(screen.getByRole('form', { name: /instrument override form/i })).toBeInTheDocument();
    });

    // The baked NAS100USD override (a 7-of-8 required subset) shows in the current-overrides table.
    const table = screen.getByRole('table', { name: /current overrides/i });
    expect(table).toHaveTextContent('NAS100USD');
    expect(table).toHaveTextContent('7 of 8');
  });

  it('renders the read-only global concept settings (weights, risk, execution, scanning)', async () => {
    renderWithProviders(<SettingsPage />);

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /confluence & grading/i })).toBeInTheDocument();
    });

    // The weighted §2.5.3 universe is surfaced (the top weight is KillzoneEntry 1.00).
    const weights = screen.getByRole('table', { name: /confluence weights/i });
    expect(weights).toHaveTextContent('KillzoneEntry');
    expect(weights).toHaveTextContent('1.00');

    // Risk + execution rows render the resolved §2.5.5/§5.4 values.
    expect(screen.getByRole('heading', { name: /risk/i })).toBeInTheDocument();
    expect(screen.getByText(/loss ladder/i)).toBeInTheDocument();
    expect(screen.getByText('0.5% → 0.25%')).toBeInTheDocument(); // exact, un-rounded ladder
  });

  it('blocks a required subset that omits DisplacementMss (the FSM direction lock)', async () => {
    renderWithProviders(<SettingsPage />);

    await waitFor(() => {
      expect(screen.getByRole('form', { name: /instrument override form/i })).toBeInTheDocument();
    });

    // Select a single required condition that is NOT the direction lock, then save.
    fireEvent.click(screen.getByRole('button', { name: 'BiasAligned' }));
    fireEvent.submit(screen.getByRole('form', { name: /instrument override form/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/must include DisplacementMss/i);
  });

  it('has no execute/order control (read-only §6.3)', async () => {
    renderWithProviders(<SettingsPage />);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /save override/i })).toBeInTheDocument();
    });
    expect(
      screen.queryByRole('button', { name: /execute|place order|buy|sell|go live/i }),
    ).toBeNull();
  });
});
