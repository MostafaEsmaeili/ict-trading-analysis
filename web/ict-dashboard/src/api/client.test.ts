// client live-path tests (finding [8] + [7] posture). The overlay live path must consume the overlays
// the host already returns on the SAME /api/chart ChartResponse (mapped via setupToOverlays, filtered to
// the requested timeframe) — NOT a dedicated endpoint that always throws. And a failed live fetch must
// THROW (fail hard), never silently serve fixtures.
//
// USE_MOCKS is read from import.meta.env at module load, so each test stubs the env, resets the module
// registry, and dynamically imports a fresh client bound to that flag.
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ChartResponse, EquityPointDto, SetupDto } from '../types/api';

function setup(id: string, triggerTimeframe: string): SetupDto {
  return {
    id,
    symbol: 'EURUSD',
    direction: 'Bullish',
    killzone: 'LondonOpen',
    style: 'Intraday',
    grade: 'A',
    score: 82,
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
  it('maps only the MOST-RECENT same-timeframe setup (declutter), dropping older + wrong-TF setups', async () => {
    const response: ChartResponse = {
      symbol: 'EURUSD',
      timeframe: 'M5',
      style: 'Intraday',
      candles: [],
      // An OLDER M5 setup + a NEWER M5 setup + an H1 setup. Only the newest M5 ('b') is drawn: older
      // M5 ('a') is decluttered away and the H1 ('c') is filtered out (wrong timeframe).
      overlays: [
        { ...setup('a', 'M5'), detectedAtUtc: '2026-06-19T05:00:00Z' },
        { ...setup('b', 'M5'), detectedAtUtc: '2026-06-19T06:50:00Z' },
        { ...setup('c', 'H1'), detectedAtUtc: '2026-06-19T07:00:00Z' },
      ],
    };
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => ({ ok: true, json: async () => response })),
    );

    const { fetchOverlays } = await liveClient();
    const overlays = await fetchOverlays('EURUSD', 'M5', 'Intraday');

    // setupToOverlays emits 2 overlays per setup (tradeLevels + drawOnLiquidity); only the newest M5 → 2.
    expect(overlays).toHaveLength(2);
    expect(overlays.every((o) => 'setupId' in o && o.setupId === 'b')).toBe(true);
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

describe('client live equity curve', () => {
  it('GETs /api/equity and returns the points (no longer throws "not available until WP7")', async () => {
    // The host now serves the equity curve over REST (it is wired — the old stub threw unconditionally).
    const points: EquityPointDto[] = [
      { atUtc: '2026-06-19T06:00:00Z', equity: 0 },
      { atUtc: '2026-06-19T07:00:00Z', equity: 2.6 },
      { atUtc: '2026-06-19T08:00:00Z', equity: 1.4 },
    ];
    // Typed arg so mock.calls[0][0] (the requested URL) is in-bounds under strict tuple checking.
    const fetchSpy = vi.fn(async (_input?: unknown) => ({ ok: true, json: async () => points }));
    vi.stubGlobal('fetch', fetchSpy);

    const { fetchEquityCurve } = await liveClient();
    const result = await fetchEquityCurve();

    // Hit the real endpoint (mirrors fetchPerformance → /api/performance) and pass the points through.
    expect(fetchSpy).toHaveBeenCalledTimes(1);
    expect(String(fetchSpy.mock.calls[0][0])).toContain('/api/equity');
    expect(result).toEqual(points);
  });

  it('FAILS HARD (throws) on a live equity fetch error — never silently serves fixtures', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => ({ ok: false, status: 500, json: async () => ({}) })),
    );

    const { fetchEquityCurve } = await liveClient();
    await expect(fetchEquityCurve()).rejects.toThrow(/500/);
  });
});
