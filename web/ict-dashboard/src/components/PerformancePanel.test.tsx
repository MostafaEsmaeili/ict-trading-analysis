// PerformancePanel R-unit rendering (findings [2]/[9]/[10]/[11]): the wire is R-based, not
// dollars/percent. Max DD is a positive R magnitude (not a percent), the profit-factor sentinel
// renders as ∞, and winRate stays a true 0..1 fraction.
import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { PerformancePanel } from './PerformancePanel';
import {
  MOCK_EQUITY_CURVE,
  MOCK_PERFORMANCE,
  MOCK_PERFORMANCE_BY_MODEL,
  MOCK_PERFORMANCE_NO_LOSSES,
} from '../mocks/fixtures';

describe('PerformancePanel', () => {
  it('renders max drawdown in R units, not a percent', () => {
    render(
      <PerformancePanel summary={MOCK_PERFORMANCE} equityCurve={MOCK_EQUITY_CURVE} isLoading={false} />,
    );
    expect(screen.getByText('Max DD (R)')).toBeInTheDocument();
    // 3.2 R magnitude → "3.20R" (never "3.2%").
    expect(screen.getByText('3.20R')).toBeInTheDocument();
    expect(screen.queryByText('3.2%')).toBeNull();
  });

  it('keeps win rate as a 0..1 fraction rendered as a percent', () => {
    render(
      <PerformancePanel summary={MOCK_PERFORMANCE} equityCurve={MOCK_EQUITY_CURVE} isLoading={false} />,
    );
    expect(screen.getByText('62.5%')).toBeInTheDocument();
  });

  it('renders a finite profit factor verbatim', () => {
    render(
      <PerformancePanel summary={MOCK_PERFORMANCE} equityCurve={MOCK_EQUITY_CURVE} isLoading={false} />,
    );
    expect(screen.getByText('2.34')).toBeInTheDocument();
  });

  it('renders the no-losses profit-factor sentinel as ∞, not 999999', () => {
    render(
      <PerformancePanel
        summary={MOCK_PERFORMANCE_NO_LOSSES}
        equityCurve={MOCK_EQUITY_CURVE}
        isLoading={false}
      />,
    );
    expect(screen.getByText('∞')).toBeInTheDocument();
    expect(screen.queryByText(/999999/)).toBeNull();
  });

  it('renders n/a profit factor when there are no trades', () => {
    render(
      <PerformancePanel
        summary={{
          tradeCount: 0,
          winRate: 0,
          averageR: 0,
          profitFactor: 0,
          expectancy: 0,
          maxDrawdown: 0,
        }}
        equityCurve={[]}
        isLoading={false}
      />,
    );
    expect(screen.getByText('n/a')).toBeInTheDocument();
  });

  it('renders the per-model comparison table when more than one model has trades', () => {
    render(
      <PerformancePanel
        summary={MOCK_PERFORMANCE}
        equityCurve={MOCK_EQUITY_CURVE}
        isLoading={false}
        modelPerformance={MOCK_PERFORMANCE_BY_MODEL}
      />,
    );
    const table = screen.getByRole('table', { name: /performance by model/i });
    expect(within(table).getByText('ICT 2022')).toBeInTheDocument();
    expect(within(table).getByText('ICT 2024')).toBeInTheDocument();
    // Each model row carries its own trade count from the breakdown fixture (16 / 8).
    expect(within(table).getByText('16')).toBeInTheDocument();
    expect(within(table).getByText('8')).toBeInTheDocument();
  });

  it('omits the per-model comparison when only one model has trades', () => {
    render(
      <PerformancePanel
        summary={MOCK_PERFORMANCE}
        equityCurve={MOCK_EQUITY_CURVE}
        isLoading={false}
        modelPerformance={[MOCK_PERFORMANCE_BY_MODEL[0]]}
      />,
    );
    expect(screen.queryByRole('table', { name: /performance by model/i })).toBeNull();
  });
});
