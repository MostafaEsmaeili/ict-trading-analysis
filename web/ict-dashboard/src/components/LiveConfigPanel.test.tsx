// LiveConfigPanel — the Live-page account+config card (plan §15 §3). Shows the provider, the risk %,
// the equity + Δ%, the open-risk-vs-cap utilization and the style/killzone chips. Read-only.
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { LiveConfigPanel } from './LiveConfigPanel';
import { MOCK_ACCOUNT, MOCK_CONFIG } from '../mocks/fixtures';

describe('LiveConfigPanel', () => {
  it('renders the account equity, risk-utilization and config chips', () => {
    render(
      <LiveConfigPanel config={MOCK_CONFIG} account={MOCK_ACCOUNT} isLoading={false} />,
    );

    // Equity + the open-risk utilization percent appear.
    expect(screen.getByText(/equity/i)).toBeInTheDocument();
    expect(screen.getByText(/53\.4%/)).toBeInTheDocument();

    // The active styles + killzones surface as chips.
    expect(screen.getByText('Intraday')).toBeInTheDocument();
    expect(screen.getByText('LondonOpen')).toBeInTheDocument();
    expect(screen.getByText('NewYorkOpen')).toBeInTheDocument();

    // Provider chip.
    expect(screen.getByText('Replay')).toBeInTheDocument();
  });

  it('shows a visible error state when the account/config queries fail (§6.3)', () => {
    render(
      <LiveConfigPanel
        config={undefined}
        account={undefined}
        isLoading={false}
        isError
        error={new Error('account host error')}
      />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent(/account host error/i);
  });

  it('has no deposit/withdraw/execute control', () => {
    render(<LiveConfigPanel config={MOCK_CONFIG} account={MOCK_ACCOUNT} isLoading={false} />);
    expect(
      screen.queryByRole('button', { name: /deposit|withdraw|execute|buy|sell/i }),
    ).toBeNull();
  });
});
