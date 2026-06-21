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

  it('derives trade-level + draw overlays from a streamed setup', () => {
    const setup: SetupDto = {
      id: 'x',
      symbol: 'EURUSD',
      direction: 'Bullish',
      killzone: 'LondonOpen',
      style: 'Intraday',
      grade: 'A',
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
  });
});
