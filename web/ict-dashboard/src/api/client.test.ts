// client live-path tests (finding [8] + [7] posture). The overlay live path must consume the overlays
// the host already returns on the SAME /api/chart ChartResponse (mapped via setupToOverlays, filtered to
// the requested timeframe) — NOT a dedicated endpoint that always throws. And a failed live fetch must
// THROW (fail hard), never silently serve fixtures.
//
// USE_MOCKS is read from import.meta.env at module load, so each test stubs the env, resets the module
// registry, and dynamically imports a fresh client bound to that flag.
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ChartResponse, SetupDto } from '../types/api';

function setup(id: string, triggerTimeframe: string): SetupDto {
  return {
    id,
    symbol: 'EURUSD',
    direction: 'Bullish',
    killzone: 'LondonOpen',
    style: 'Intraday',
    grade: 'A',
    triggerTimeframe,
    entry: 1.0724,
    stop: 1.0689,
    targets: [1.0762, 1.079],
    rewardRatio: 2.6,
    reason: 'r',
    detectedAtUtc: '2026-06-19T06:50:00Z',
    isAdvisoryOnly: true,
  };
}

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  vi.resetModules();
});

async function liveClient() {
  vi.stubEnv('VITE_USE_MOCKS', 'false');
  vi.resetModules();
  return import('./client');
}

describe('client live overlays', () => {
  it('maps host ChartResponse overlays to chart overlays, filtered to the timeframe', async () => {
    const response: ChartResponse = {
      symbol: 'EURUSD',
      timeframe: 'M5',
      style: 'Intraday',
      candles: [],
      // Two M5 setups (kept) + one H1 setup (must be filtered out — wrong timeframe).
      overlays: [setup('a', 'M5'), setup('b', 'M5'), setup('c', 'H1')],
    };
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => ({ ok: true, json: async () => response })),
    );

    const { fetchOverlays } = await liveClient();
    const overlays = await fetchOverlays('EURUSD', 'M5', 'Intraday');

    // setupToOverlays emits 2 overlays per setup (tradeLevels + drawOnLiquidity); 2 M5 setups → 4.
    expect(overlays).toHaveLength(4);
    expect(overlays.every((o) => 'setupId' in o && (o.setupId === 'a' || o.setupId === 'b'))).toBe(
      true,
    );
  });

  it('FAILS HARD (throws) on a live fetch error — never silently serves fixtures', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => ({ ok: false, status: 503, json: async () => ({}) })),
    );

    const { fetchOverlays } = await liveClient();
    await expect(fetchOverlays('EURUSD', 'M5', 'Intraday')).rejects.toThrow(/503/);
  });
});
