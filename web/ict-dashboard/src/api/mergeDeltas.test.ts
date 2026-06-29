// Pure cache-merge unit tests (plan §9 — SignalR deltas merged via setQueryData). These prove the merge
// helpers behave correctly without a QueryClient or a socket: upsert by id, drop closed trades from the
// active list, update the forming candle in place, and derive overlays from a streamed setup.
import { describe, expect, it } from 'vitest';
import { appendAlert, appendCandle, mergeSetupOverlays, upsertTrade } from './mergeDeltas';
import type { AlertDto, CandleDto, PaperTradeDto, SetupDto } from '../types/api';

const alert = (id: string): AlertDto => ({
  id,
  kind: 'SetupConfirmed',
  symbol: 'EURUSD',
  message: 'm',
  direction: 'Bullish',
  killzone: 'LondonOpen',
  style: 'Intraday',
  atUtc: '2026-06-19T06:00:00Z',
});

const trade = (id: string, status: string): PaperTradeDto => ({
  id,
  setupId: 's',
  symbol: 'EURUSD',
  direction: 'Long',
  status,
  style: 'Intraday',
  killzone: 'LondonOpen',
  entry: 1.07,
  stop: 1.069,
  targets: [1.072],
  size: 0.5,
  openedAtUtc: '2026-06-19T06:00:00Z',
  closedAtUtc: null,
  realizedR: null,
  lifecycle: 'Open',
  closeReason: null,
  netR: null,
  grossPnl: null,
  costs: null,
  netPnl: null,
  hasScaledOut: false,
  isBreakevenArmed: false,
  riskBudget: 100,
  timeframe: 'M5',
  currentStop: 1.069,
  exitPrice: null,
  managedFromUtc: '2026-06-19T06:00:00Z',
});

const candle = (time: string, close: number): CandleDto => ({
  symbol: 'EURUSD',
  timeframe: 'M5',
  openTimeUtc: time,
  open: 1.07,
  high: 1.071,
  low: 1.069,
  close,
  volume: 100,
});

describe('mergeDeltas', () => {
  it('prepends an alert newest-first and de-dups by id', () => {
    const after = appendAlert([alert('a')], alert('a'));
    expect(after).toHaveLength(1);
    const two = appendAlert([alert('a')], alert('b'));
    expect(two.map((x) => x.id)).toEqual(['b', 'a']);
  });

  it('drops a closed trade from the active list', () => {
    const open = upsertTrade([trade('t1', 'Open')], trade('t2', 'Open'));
    expect(open).toHaveLength(2);
    const closed = upsertTrade(open, trade('t1', 'Closed'));
    expect(closed.map((x) => x.id)).toEqual(['t2']);
  });

  it('updates the forming candle in place but appends a new bar', () => {
    const base = [candle('2026-06-19T06:00:00Z', 1.07)];
    const updated = appendCandle(base, candle('2026-06-19T06:00:00Z', 1.0712));
    expect(updated).toHaveLength(1);
    expect(updated[0].close).toBe(1.0712);

    const appended = appendCandle(updated, candle('2026-06-19T06:05:00Z', 1.072));
    expect(appended).toHaveLength(2);
  });

  it('inserts an out-of-order bar in ascending time order (never appends non-monotonic)', () => {
    const base = [
      candle('2026-06-19T06:00:00Z', 1.07),
      candle('2026-06-19T06:10:00Z', 1.072),
    ];
    // A late/redelivered bar BETWEEN the two existing bars must be inserted, not appended.
    const merged = appendCandle(base, candle('2026-06-19T06:05:00Z', 1.071));
    expect(merged.map((c) => c.openTimeUtc)).toEqual([
      '2026-06-19T06:00:00Z',
      '2026-06-19T06:05:00Z',
      '2026-06-19T06:10:00Z',
    ]);
  });

  it('upserts an out-of-order duplicate in place rather than duplicating', () => {
    const base = [
      candle('2026-06-19T06:00:00Z', 1.07),
      candle('2026-06-19T06:05:00Z', 1.071),
      candle('2026-06-19T06:10:00Z', 1.072),
    ];
    // A redelivery of a NON-last existing bar must replace it (not append, not duplicate).
    const merged = appendCandle(base, candle('2026-06-19T06:05:00Z', 1.0715));
    expect(merged).toHaveLength(3);
    expect(merged[1].close).toBe(1.0715);
    expect(merged.map((c) => c.openTimeUtc)).toEqual([
      '2026-06-19T06:00:00Z',
      '2026-06-19T06:05:00Z',
      '2026-06-19T06:10:00Z',
    ]);
  });

  it('caps the series at MAX_CANDLES (1500), dropping the oldest bars', () => {
    let list: CandleDto[] = [];
    for (let i = 0; i < 1600; i++) {
      // Sortable, strictly-increasing ISO times (minute resolution over ~26h is fine for the test).
      const time = new Date(Date.parse('2026-06-19T00:00:00Z') + i * 60_000).toISOString();
      list = appendCandle(list, candle(time, 1.07 + i * 1e-5));
    }
    expect(list).toHaveLength(1500);
    // The newest bar is retained; the oldest 100 were evicted.
    expect(list[list.length - 1].openTimeUtc).toBe(
      new Date(Date.parse('2026-06-19T00:00:00Z') + 1599 * 60_000).toISOString(),
    );
    expect(list[0].openTimeUtc).toBe(
      new Date(Date.parse('2026-06-19T00:00:00Z') + 100 * 60_000).toISOString(),
    );
  });

  it('derives trade-level + draw overlays from a streamed setup', () => {
    const setup: SetupDto = {
      id: 'x',
      symbol: 'EURUSD',
      direction: 'Bullish',
      killzone: 'LondonOpen',
      style: 'Intraday',
      grade: 'A',
      score: 82,
      triggerTimeframe: 'M5',
      entry: 1.0724,
      stop: 1.0689,
      targets: [1.0762, 1.079],
      rewardRatio: 2.6,
      reason: 'r',
      detectedAtUtc: '2026-06-19T06:50:00Z',
      isAdvisoryOnly: true,
    };
    const overlays = mergeSetupOverlays([], setup);
    expect(overlays.map((o) => o.kind)).toEqual(['tradeLevels', 'drawOnLiquidity']);
    expect(overlays.every((o) => 'setupId' in o && o.setupId === 'x')).toBe(true);
  });

  it('de-dups overlays by setup id (a redelivered setup REPLACES, not stacks)', () => {
    const setup: SetupDto = {
      id: 'dup',
      symbol: 'EURUSD',
      direction: 'Bullish',
      killzone: 'LondonOpen',
      style: 'Intraday',
      grade: 'A',
      score: 82,
      triggerTimeframe: 'M5',
      entry: 1.0724,
      stop: 1.0689,
      targets: [1.0762, 1.079],
      rewardRatio: 2.6,
      reason: 'r',
      detectedAtUtc: '2026-06-19T06:50:00Z',
      isAdvisoryOnly: true,
    };
    const once = mergeSetupOverlays([], setup);
    const twice = mergeSetupOverlays(once, setup);
    // Same id merged twice yields the SAME count (2), not 4.
    expect(twice).toHaveLength(2);

    // A DIFFERENT setup id stacks alongside (its own 2 overlays).
    const other = mergeSetupOverlays(twice, { ...setup, id: 'other' });
    expect(other).toHaveLength(4);
  });
});
