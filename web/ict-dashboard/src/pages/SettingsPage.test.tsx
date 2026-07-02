// SettingsPage — live per-instrument tuning + the read-only global concept view (plan §15), reorganised
// into tabs (Quick setup · Instruments · Concept model · Discovery · Calendar) for a non-expert. The
// per-instrument form is editable (k-of-n / required subset / per-pair costs); a required subset must
// include DisplacementMss (the FSM direction lock). Read-only/advisory: no order/execute control (§6.3).
//
// Content that now lives behind a tab is asserted AFTER activating that tab.
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { SettingsPage } from './SettingsPage';
import { renderWithProviders } from '../test/renderWithProviders';
import { __resetMockSettingsForTest } from '../mocks/fixtures';

/** Click a settings tab by its label. */
function openTab(label: RegExp): void {
  fireEvent.click(screen.getByRole('tab', { name: label }));
}

// The mock PUT mutates MOCK_SETTINGS in place; reset between tests so a saved override doesn't leak.
beforeEach(() => __resetMockSettingsForTest());
afterEach(() => __resetMockSettingsForTest());

describe('SettingsPage', () => {
  it('renders the per-instrument form (the default Quick-setup tab) and the overrides table on Instruments', async () => {
    renderWithProviders(<SettingsPage />);

    // The Quick-setup tab is the default landing — the form renders without switching tabs.
    await waitFor(() => {
      expect(screen.getByRole('form', { name: /instrument override form/i })).toBeInTheDocument();
    });

    // The baked NAS100USD override (a 7-of-8 required subset) shows in the Instruments tab's table.
    openTab(/instruments/i);
    const table = screen.getByRole('table', { name: /current overrides/i });
    expect(table).toHaveTextContent('NAS100USD');
    expect(table).toHaveTextContent('7 of 8');
  });

  it('renders the read-only global concept settings (weights, risk, execution) on the Concept-model tab in Advanced mode', async () => {
    renderWithProviders(<SettingsPage />);

    // The weights table is Advanced-only — flip the mode switch, then open the Concept-model tab.
    await waitFor(() => {
      expect(screen.getByRole('switch', { name: /advanced mode/i })).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('switch', { name: /advanced mode/i }));
    openTab(/concept model/i);

    await waitFor(() => {
      expect(
        screen.getByRole('heading', { name: /confluence weights/i }),
      ).toBeInTheDocument();
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

  it('the Quick-setup tab lets the operator toggle the active setup models and saves them live', async () => {
    renderWithProviders(<SettingsPage />);

    // The Active setup models control renders on the default Quick-setup tab with the live selection:
    // ICT 2022 is on (and is the only one → its chip is disabled so it can't be unchecked), ICT 2024 off.
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'ICT 2022' })).toHaveAttribute('aria-pressed', 'true');
    });
    expect(screen.getByRole('button', { name: 'ICT 2022' })).toBeDisabled(); // last-on model can't be unchecked
    expect(screen.getByRole('button', { name: 'ICT 2024' })).toHaveAttribute('aria-pressed', 'false');

    // Turning ICT 2024 on saves via PUT /api/settings/scanning; the settings query re-reads the applied
    // state → both chips pressed and ICT 2022 becomes enabled again (no longer the only active model).
    fireEvent.click(screen.getByRole('button', { name: 'ICT 2024' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'ICT 2024' })).toHaveAttribute('aria-pressed', 'true');
    });
    expect(screen.getByRole('button', { name: 'ICT 2022' })).toBeEnabled();
  });

  it('the Discovery tab exposes a LIVE per-instrument Auto/Manual entry-mode control', async () => {
    renderWithProviders(<SettingsPage />);

    openTab(/discovery/i);

    // The entry-mode control is a real, enabled combobox (no longer a disabled preview).
    const select = (await screen.findByRole('combobox', { name: 'Entry mode' })) as HTMLSelectElement;
    expect(select).toBeInTheDocument();
    expect(select).toBeEnabled();
    expect(select.value).toBe('inherit');

    // Switching to Manual is accepted (the PUT mutation runs against the mock; the value reflects it).
    fireEvent.change(select, { target: { value: 'Manual' } });
    await waitFor(() => {
      expect((screen.getByRole('combobox', { name: 'Entry mode' }) as HTMLSelectElement).value).toBe(
        'Manual',
      );
    });

    // Still no execute/order control on this tab.
    expect(
      screen.queryByRole('button', { name: /execute|place order|buy|sell|go live/i }),
    ).toBeNull();
  });
});
