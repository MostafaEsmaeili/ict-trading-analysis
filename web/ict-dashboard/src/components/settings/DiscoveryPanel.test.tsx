// DiscoveryPanel — a read-only PREVIEW of the scan matrix (assets × timeframes × styles × killzones) and
// the Auto/Manual entry-mode concept. The discovery + entry-mode WRITE endpoints don't exist yet, so the
// panel must clearly badge itself a preview and not invent any execute control (§6.3).
import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { DiscoveryPanel } from './DiscoveryPanel';
import { renderWithProviders } from '../../test/renderWithProviders';

describe('DiscoveryPanel', () => {
  it('renders the scan matrix from the running config (mocks: EURUSD/GBPUSD scanned)', async () => {
    renderWithProviders(<DiscoveryPanel />);

    const matrix = await screen.findByRole('group', { name: /scan matrix/i });
    // The matrix lists the asset/timeframe/style/killzone dimensions.
    expect(matrix).toHaveTextContent('EURUSD');
    expect(matrix).toHaveTextContent('M5');
    expect(matrix).toHaveTextContent('Intraday');
    expect(matrix).toHaveTextContent('LondonOpen');
  });

  it('shows the Auto/Manual entry-mode preview', async () => {
    renderWithProviders(<DiscoveryPanel />);

    const group = await screen.findByRole('group', { name: /entry mode options/i });
    expect(group).toHaveTextContent('Auto');
    expect(group).toHaveTextContent('Manual');
  });

  it('badges the unfinished write endpoints as a preview and has no execute control', async () => {
    renderWithProviders(<DiscoveryPanel />);

    await waitFor(() => {
      expect(screen.getAllByText(/preview/i).length).toBeGreaterThan(0);
    });
    expect(screen.getByText(/live editing of the scan matrix lands/i)).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /execute|place order|buy|sell|go live/i }),
    ).toBeNull();
  });
});
