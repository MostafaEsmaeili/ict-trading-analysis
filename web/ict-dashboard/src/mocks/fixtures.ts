// ---------------------------------------------------------------------------------------------------
// Deterministic mock fixtures (plan §9 — "frontend on mocks", §11.3 Phase B). These stand in for the
// frozen REST + SignalR data until WP7 wires the live host. Shapes are the real DTOs (types/api.ts);
// overlay geometry is the §9.1 internal shape (types/overlays.ts). All times are ISO-8601 UTC.
//
// The scenario is a worked bullish EURUSD London-open setup: sweep of the Asian low → MSS with
// displacement → bullish FVG + OTE 62–79% → entry/stop/T1/T2 drawn to opposing buy-side liquidity.
// ---------------------------------------------------------------------------------------------------

import type {
  AlertDto,
  CandleDto,
  EquityPointDto,
  PaperTradeDto,
  PerformanceSummaryDto,
  SetupDto,
} from '../types/api';
import type { ChartOverlay } from '../types/overlays';

export const MOCK_SYMBOL = 'EURUSD';
export const MOCK_TIMEFRAME = 'M5';

// A small, deterministic synthetic London-open session (UTC). Prices are EURUSD-scale.
// The shape: drift down into an Asian-low sweep (the dip), an energetic up displacement, then a
// retrace into the FVG/OTE and an expansion toward the buy-side draw.
const BASE_TIME = Date.parse('2026-06-19T06:00:00Z'); // 02:00 NY (London open)
const STEP_MS = 5 * 60 * 1000;

function candle(
  i: number,
  open: number,
  high: number,
  low: number,
  close: number,
  volume: number,
): CandleDto {
  return {
    symbol: MOCK_SYMBOL,
    timeframe: MOCK_TIMEFRAME,
    openTimeUtc: new Date(BASE_TIME + i * STEP_MS).toISOString(),
    open,
    high,
    low,
    close,
    volume,
  };
}

// 18 candles: i0–4 drift, i5 sweep wick under 1.0700, i6–7 displacement up, i8–10 retrace into FVG,
// i11–17 expansion to the draw at ~1.0790.
export const MOCK_CANDLES: CandleDto[] = [
  candle(0, 1.072, 1.0725, 1.0712, 1.0716, 1200),
  candle(1, 1.0716, 1.0719, 1.0708, 1.071, 1100),
  candle(2, 1.071, 1.0712, 1.0703, 1.0705, 1300),
  candle(3, 1.0705, 1.0707, 1.0701, 1.0702, 1250),
  candle(4, 1.0702, 1.0704, 1.0699, 1.0701, 1400),
  candle(5, 1.0701, 1.0703, 1.0689, 1.0698, 2100), // sweep: wick under the 1.0700 Asian low
  candle(6, 1.0698, 1.0728, 1.0697, 1.0726, 3200), // displacement up
  candle(7, 1.0726, 1.0742, 1.0724, 1.074, 2800), // displacement up (creates FVG with c6)
  candle(8, 1.074, 1.0741, 1.0726, 1.0729, 1700), // retrace into FVG/OTE
  candle(9, 1.0729, 1.0732, 1.0721, 1.0724, 1600), // OTE 70.5%
  candle(10, 1.0724, 1.0735, 1.0723, 1.0734, 1500), // entry confirmed
  candle(11, 1.0734, 1.0752, 1.0732, 1.075, 1900),
  candle(12, 1.075, 1.0763, 1.0748, 1.0761, 2000),
  candle(13, 1.0761, 1.0769, 1.0758, 1.0762, 1750), // T1 ~1.0762
  candle(14, 1.0762, 1.0771, 1.0759, 1.0768, 1650),
  candle(15, 1.0768, 1.0783, 1.0766, 1.078, 1800),
  candle(16, 1.078, 1.0792, 1.0778, 1.0789, 1700), // T2 ~1.0790 (buy-side draw)
  candle(17, 1.0789, 1.0791, 1.0782, 1.0785, 1400),
];

const t = (i: number) => new Date(BASE_TIME + i * STEP_MS).toISOString();

export const MOCK_OVERLAYS: ChartOverlay[] = [
  { kind: 'killzone', killzone: 'LondonOpen', fromUtc: t(0), toUtc: t(17) },
  { kind: 'liquidity', side: 'sell', price: 1.07, fromUtc: t(0), label: 'Asian low', swept: true },
  { kind: 'liquidity', side: 'buy', price: 1.079, fromUtc: t(0), label: 'Buy-side draw', swept: false },
  { kind: 'sweep', direction: 'Bullish', atUtc: t(5), price: 1.0689 },
  { kind: 'mss', direction: 'Bullish', atUtc: t(7), brokenSwingPrice: 1.0725 },
  { kind: 'fvg', direction: 'Bullish', fromUtc: t(6), toUtc: t(10), top: 1.0728, bottom: 1.0721, mitigated: false },
  {
    kind: 'orderBlock',
    direction: 'Bullish',
    isBreaker: false,
    fromUtc: t(5),
    toUtc: t(10),
    top: 1.0703,
    bottom: 1.0689,
    meanThreshold: 1.0696,
  },
  {
    kind: 'ote',
    direction: 'Bullish',
    fromUtc: t(6),
    toUtc: t(10),
    band62: 1.0718,
    band79: 1.0708,
    sweetSpot705: 1.0713,
  },
  { kind: 'drawOnLiquidity', direction: 'Bullish', fromUtc: t(7), targetPrice: 1.079 },
  {
    kind: 'tradeLevels',
    direction: 'Bullish',
    entry: 1.0724,
    stop: 1.0689,
    targets: [1.0762, 1.079],
    rewardRatio: 2.6,
    entryUtc: t(10), // entry confirmed on candle 10 — the marker pins here
    symbol: MOCK_SYMBOL,
  },
];

export const MOCK_SETUPS: SetupDto[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    symbol: MOCK_SYMBOL,
    direction: 'Bullish',
    killzone: 'LondonOpen',
    style: 'Intraday',
    grade: 'A',
    triggerTimeframe: 'M5',
    entry: 1.0724,
    stop: 1.0689,
    targets: [1.0762, 1.079],
    rewardRatio: 2.6,
    reason:
      'Bullish FVG formed inside London Open killzone after Asian-low sweep; MSS confirmed with displacement; OTE 0.705. Draw on buy-side liquidity (RR 2.6).',
    detectedAtUtc: t(10),
    isAdvisoryOnly: true,
  },
];

export const MOCK_ALERTS: AlertDto[] = [
  {
    id: 'a1111111-1111-1111-1111-111111111111',
    kind: 'SetupConfirmed',
    symbol: MOCK_SYMBOL,
    message:
      'Bullish FVG formed inside London Open killzone after Asian-low sweep, MSS confirmed, OTE 0.705 — RR 2.6 to buy-side draw.',
    direction: 'Bullish',
    killzone: 'LondonOpen',
    style: 'Intraday',
    atUtc: t(10),
  },
  {
    id: 'a2222222-2222-2222-2222-222222222222',
    kind: 'SetupConfirmed',
    symbol: 'GBPUSD',
    message:
      'Bearish order block tapped in New York Open killzone after buy-side sweep; MSS down, OTE 0.70 — RR 3.1 to sell-side draw.',
    direction: 'Bearish',
    killzone: 'NewYorkOpen',
    style: 'Scalp',
    atUtc: new Date(BASE_TIME + 30 * STEP_MS).toISOString(),
  },
];

export const MOCK_ACTIVE_TRADES: PaperTradeDto[] = [
  {
    id: 'c1111111-1111-1111-1111-111111111111',
    setupId: '11111111-1111-1111-1111-111111111111',
    symbol: MOCK_SYMBOL,
    direction: 'Long',
    status: 'Open',
    style: 'Intraday',
    killzone: 'LondonOpen',
    entry: 1.0724,
    stop: 1.0689,
    targets: [1.0762, 1.079],
    size: 0.5,
    openedAtUtc: t(10),
    closedAtUtc: null,
    realizedR: null,
  },
  {
    id: 'c2222222-2222-2222-2222-222222222222',
    setupId: '22222222-2222-2222-2222-222222222222',
    symbol: 'GBPUSD',
    direction: 'Short',
    status: 'Open',
    style: 'Scalp',
    killzone: 'NewYorkOpen',
    entry: 1.272,
    stop: 1.2745,
    targets: [1.2695, 1.2655],
    size: 0.3,
    openedAtUtc: new Date(BASE_TIME + 30 * STEP_MS).toISOString(),
    closedAtUtc: null,
    realizedR: null,
  },
];

// Units match the live wire contract (PerformanceCalculator): winRate a 0..1 fraction; averageR /
// expectancy / maxDrawdown in R; maxDrawdown a POSITIVE absolute peak-to-trough magnitude;
// profitFactor a plain ratio (999999 = the "no losses" sentinel — exercised by MOCK_PERFORMANCE_NO_LOSSES).
export const MOCK_PERFORMANCE: PerformanceSummaryDto = {
  tradeCount: 24,
  winRate: 0.625,
  averageR: 1.18,
  profitFactor: 2.34,
  expectancy: 0.74,
  maxDrawdown: 3.2,
};

// Edge fixture: an all-wins / no-losses book → the backend UndefinedProfitFactor (999999) sentinel,
// which the panel renders as "∞".
export const MOCK_PERFORMANCE_NO_LOSSES: PerformanceSummaryDto = {
  tradeCount: 3,
  winRate: 1,
  averageR: 2.1,
  profitFactor: 999999,
  expectancy: 2.1,
  maxDrawdown: 0,
};

// Cumulative R from a zero baseline (running ΣR), matching EquityPointDto.equity on the wire.
export const MOCK_EQUITY_CURVE: EquityPointDto[] = [
  { atUtc: '2026-06-12T20:00:00Z', equity: 0 },
  { atUtc: '2026-06-13T20:00:00Z', equity: 1.8 },
  { atUtc: '2026-06-14T20:00:00Z', equity: 0.9 },
  { atUtc: '2026-06-15T20:00:00Z', equity: 4.1 },
  { atUtc: '2026-06-16T20:00:00Z', equity: 3.5 },
  { atUtc: '2026-06-17T20:00:00Z', equity: 7.2 },
  { atUtc: '2026-06-18T20:00:00Z', equity: 6.05 },
  { atUtc: '2026-06-19T20:00:00Z', equity: 9.8 },
];
