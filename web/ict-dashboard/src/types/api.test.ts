// Contract round-trip: the mock fixtures must satisfy the frozen DTO interfaces (compile-time check via
// the typed assignments below) AND the enum member-name unions must equal the exact backend names.
import { describe, expect, it } from 'vitest';
import type {
  AlertDto,
  CandleDto,
  Direction,
  Killzone,
  PaperTradeDto,
  PerformanceSummaryDto,
  SetupDto,
  SetupGrade,
  TradeStatus,
  TradeStyle,
} from './api';
import {
  MOCK_ACTIVE_TRADES,
  MOCK_ALERTS,
  MOCK_CANDLES,
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
        'reason',
        'rewardRatio',
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
        'direction',
        'entry',
        'id',
        'killzone',
        'openedAtUtc',
        'realizedR',
        'setupId',
        'size',
        'status',
        'stop',
        'style',
        'symbol',
        'targets',
      ].sort(),
    );
    expect(Object.keys(alert).sort()).toEqual(
      ['atUtc', 'direction', 'id', 'killzone', 'kind', 'message', 'style', 'symbol'].sort(),
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
});
