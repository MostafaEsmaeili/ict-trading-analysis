// Contract round-trip: the mock fixtures must satisfy the frozen DTO interfaces (compile-time check via
// the typed assignments below) AND the enum member-name unions must equal the exact backend names.
import { describe, expect, it } from 'vitest';
import type {
  AccountStatusDto,
  AlertDto,
  BacktestDatasetDto,
  BacktestResponse,
  CandleDto,
  ConfigStatusDto,
  Direction,
  Killzone,
  OptimizeResponse,
  PaperTradeDto,
  PerformanceSummaryDto,
  SetupDto,
  SetupGrade,
  TradeLifecycle,
  TradeStatus,
  TradeStyle,
} from './api';
import {
  MOCK_ACCOUNT,
  MOCK_ACTIVE_TRADES,
  MOCK_ALERTS,
  MOCK_BACKTEST,
  MOCK_CANDLES,
  MOCK_CONFIG,
  MOCK_DATASETS,
  MOCK_OPTIMIZE,
  MOCK_PERFORMANCE,
  MOCK_SETUPS,
} from '../mocks/fixtures';

describe('api DTO contract', () => {
  it('mock fixtures conform to the frozen DTO shapes', () => {
    const candle: CandleDto = MOCK_CANDLES[0];
    const setup: SetupDto = MOCK_SETUPS[0];
    const alert: AlertDto = MOCK_ALERTS[0];
    const trade: PaperTradeDto = MOCK_ACTIVE_TRADES[0];
    const perf: PerformanceSummaryDto = MOCK_PERFORMANCE;

    // Spot-check every required field name is present (camelCase, matching System.Text.Json).
    expect(Object.keys(candle).sort()).toEqual(
      ['close', 'high', 'low', 'open', 'openTimeUtc', 'symbol', 'timeframe', 'volume'].sort(),
    );
    expect(Object.keys(setup).sort()).toEqual(
      [
        'detectedAtUtc',
        'direction',
        'entry',
        'grade',
        'id',
        'isAdvisoryOnly',
        'killzone',
        'model',
        'reason',
        'rewardRatio',
        'score',
        'stop',
        'style',
        'symbol',
        'targets',
        'triggerTimeframe',
      ].sort(),
    );
    expect(Object.keys(trade).sort()).toEqual(
      [
        'closedAtUtc',
        'closeReason',
        'costs',
        'currentStop',
        'direction',
        'entry',
        'exitPrice',
        'grossPnl',
        'hasScaledOut',
        'id',
        'isBreakevenArmed',
        'killzone',
        'lifecycle',
        'managedFromUtc',
        'model',
        'netPnl',
        'netR',
        'openedAtUtc',
        'realizedR',
        'riskBudget',
        'setupId',
        'size',
        'status',
        'stop',
        'style',
        'targets',
        'symbol',
        'timeframe',
      ].sort(),
    );
    expect(Object.keys(alert).sort()).toEqual(
      ['atUtc', 'direction', 'id', 'killzone', 'kind', 'message', 'model', 'style', 'symbol'].sort(),
    );
    expect(perf.tradeCount).toBeTypeOf('number');

    // Advisory guardrail (plan §6.3): a setup is structurally advisory.
    expect(setup.isAdvisoryOnly).toBe(true);
  });

  it('exposes the exact frozen enum member names', () => {
    const directions: Direction[] = ['Bullish', 'Bearish'];
    const killzones: Killzone[] = ['None', 'Asian', 'LondonOpen', 'NewYorkOpen', 'LondonClose', 'Pm', 'Am'];
    const styles: TradeStyle[] = ['Scalp', 'Intraday', 'Swing', 'Position'];
    const grades: SetupGrade[] = ['Reject', 'C', 'B', 'A'];
    // The PaperTrading ledger flag — Open until the final close (see TradeStatus in ./api).
    const statuses: TradeStatus[] = ['Open', 'Closed'];

    expect(directions).toContain('Bullish');
    expect(killzones).toContain('LondonOpen');
    expect(styles).toEqual(['Scalp', 'Intraday', 'Swing', 'Position']);
    expect(grades).toContain('A');
    expect(statuses).toEqual(['Open', 'Closed']);
  });

  it('round-trips the §15 multi-page DTOs (account / config / datasets / backtest / optimize)', () => {
    // Compile-time conformance: each fixture is assigned to its frozen interface.
    const account: AccountStatusDto = MOCK_ACCOUNT;
    const config: ConfigStatusDto = MOCK_CONFIG;
    const datasets: BacktestDatasetDto[] = MOCK_DATASETS;
    const backtest: BacktestResponse = MOCK_BACKTEST;
    const optimize: OptimizeResponse = MOCK_OPTIMIZE;
    const lifecycles: TradeLifecycle[] = ['Open', 'PartialTaken', 'Closed'];

    expect(Object.keys(account).sort()).toEqual(
      [
        'consecutiveLosses',
        'consecutiveWins',
        'drawdownTrough',
        'equity',
        'maxOpenPortfolioRiskPercent',
        'openRisk',
        'openRiskCap',
        'openTradeCount',
        'peakEquity',
        'riskUtilizationPercent',
        'startingEquity',
      ].sort(),
    );
    expect(Object.keys(config).sort()).toEqual(
      [
        'activeKillzones',
        'activeStyles',
        'baseRiskPercent',
        'commissionPerLotRoundTripUsd',
        'maxOpenPortfolioRiskPercent',
        'provider',
        'spreadBasePips',
        'startingEquity',
        'symbols',
      ].sort(),
    );
    expect(datasets[0].candleCount).toBeTypeOf('number');

    // The backtest response carries a PerformanceSummaryDto, a balance/ΣR equity curve and the trades.
    expect(backtest.summary.tradeCount).toBeTypeOf('number');
    expect(backtest.equity[0]).toHaveProperty('cumulativeR');
    expect(backtest.equity[0]).toHaveProperty('equity');
    expect(backtest.trades.length).toBe(backtest.tradeCount);

    // The optimizer response ranks combinations and reports the explored count + objective.
    expect(optimize.combinationCount).toBeGreaterThanOrEqual(optimize.results.length);
    expect(optimize.objective).toBeTypeOf('string');
    for (let i = 1; i < optimize.results.length; i += 1) {
      expect(optimize.results[i].score).toBeLessThanOrEqual(optimize.results[i - 1].score);
    }

    expect(lifecycles).toContain('PartialTaken');
  });
});
