// ---------------------------------------------------------------------------------------------------
// Deterministic mock fixtures (plan §9 — "frontend on mocks", §11.3 Phase B). These stand in for the
// frozen REST + SignalR data until WP7 wires the live host. Shapes are the real DTOs (types/api.ts);
// overlay geometry is the §9.1 internal shape (types/overlays.ts). All times are ISO-8601 UTC.
//
// The scenario is a worked bullish EURUSD London-open setup: sweep of the Asian low → MSS with
// displacement → bullish FVG + OTE 62–79% → entry/stop/T1/T2 drawn to opposing buy-side liquidity.
// ---------------------------------------------------------------------------------------------------

import type {
  AccountStatusDto,
  AlertDto,
  BacktestDatasetDto,
  BacktestResponse,
  CandleDto,
  ConfigStatusDto,
  EquityPointDto,
  OptimizeResponse,
  PaperTradeDto,
  PerformanceSummaryDto,
  SettingsDto,
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
    lifecycle: 'Open',
    closeReason: null,
    netR: null,
    grossPnl: null,
    costs: null,
    netPnl: null,
    hasScaledOut: false,
    isBreakevenArmed: false,
    riskBudget: 175,
    timeframe: MOCK_TIMEFRAME,
    currentStop: 1.0689,
    exitPrice: null,
    managedFromUtc: t(11),
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
    lifecycle: 'PartialTaken',
    closeReason: null,
    netR: null,
    grossPnl: null,
    costs: null,
    netPnl: null,
    hasScaledOut: true,
    isBreakevenArmed: true,
    riskBudget: 105,
    timeframe: 'M5',
    currentStop: 1.272,
    exitPrice: null,
    managedFromUtc: new Date(BASE_TIME + 31 * STEP_MS).toISOString(),
  },
];

// A closed-trade history (Trades page + the backtest table). Mix of win/loss/breakeven, all paper.
function closedTrade(
  id: string,
  symbol: string,
  direction: 'Long' | 'Short',
  style: PaperTradeDto['style'],
  killzone: string,
  entry: number,
  stop: number,
  targets: number[],
  openMin: number,
  closeMin: number,
  realizedR: number,
  closeReason: 'TargetHit' | 'StopHit' | 'TimeExit' | 'Manual',
  exitPrice: number,
): PaperTradeDto {
  const riskBudget = 100;
  const grossPnl = realizedR * riskBudget;
  const costs = 4.2;
  const netPnl = grossPnl - costs;
  return {
    id,
    setupId: `s-${id}`,
    symbol,
    direction,
    status: 'Closed',
    style,
    killzone,
    entry,
    stop,
    targets,
    size: 0.4,
    openedAtUtc: new Date(BASE_TIME + openMin * STEP_MS).toISOString(),
    closedAtUtc: new Date(BASE_TIME + closeMin * STEP_MS).toISOString(),
    realizedR,
    lifecycle: 'Closed',
    closeReason,
    netR: netPnl / riskBudget,
    grossPnl,
    costs,
    netPnl,
    hasScaledOut: closeReason === 'TargetHit',
    isBreakevenArmed: realizedR >= 0,
    riskBudget,
    timeframe: 'M5',
    currentStop: stop,
    exitPrice,
    managedFromUtc: new Date(BASE_TIME + (openMin + 1) * STEP_MS).toISOString(),
  };
}

export const MOCK_CLOSED_TRADES: PaperTradeDto[] = [
  closedTrade('d1111111-1111-1111-1111-111111111111', MOCK_SYMBOL, 'Long', 'Intraday', 'LondonOpen', 1.0724, 1.0689, [1.0762, 1.079], 10, 16, 2.5, 'TargetHit', 1.079),
  closedTrade('d2222222-2222-2222-2222-222222222222', 'GBPUSD', 'Short', 'Scalp', 'NewYorkOpen', 1.272, 1.2745, [1.2695, 1.2655], 30, 36, -1.0, 'StopHit', 1.2745),
  closedTrade('d3333333-3333-3333-3333-333333333333', MOCK_SYMBOL, 'Long', 'Intraday', 'LondonOpen', 1.081, 1.0788, [1.0832, 1.0855], 50, 70, 0.95, 'TimeExit', 1.0831),
  closedTrade('d4444444-4444-4444-4444-444444444444', 'USDJPY', 'Long', 'Swing', 'NewYorkOpen', 156.42, 156.05, [156.9, 157.6], 90, 140, 1.8, 'TargetHit', 157.6),
  closedTrade('d5555555-5555-5555-5555-555555555555', 'XAUUSD', 'Short', 'Intraday', 'LondonOpen', 2342.5, 2349.0, [2330.0, 2318.0], 120, 150, -1.0, 'StopHit', 2349.0),
  closedTrade('d6666666-6666-6666-6666-666666666666', MOCK_SYMBOL, 'Long', 'Intraday', 'LondonOpen', 1.0701, 1.0682, [1.0728, 1.0752], 160, 180, 0.02, 'Manual', 1.0701),
];

/** All trades (open + closed) — what `GET /api/trades` returns when no status filter is applied. */
export const MOCK_ALL_TRADES: PaperTradeDto[] = [...MOCK_ACTIVE_TRADES, ...MOCK_CLOSED_TRADES];

export const MOCK_ACCOUNT: AccountStatusDto = {
  startingEquity: 10000,
  equity: 10487.6,
  peakEquity: 10612.4,
  drawdownTrough: 9923.1,
  openRisk: 280,
  openRiskCap: 524.38,
  riskUtilizationPercent: 53.4,
  maxOpenPortfolioRiskPercent: 5,
  consecutiveWins: 2,
  consecutiveLosses: 0,
  openTradeCount: 2,
};

export const MOCK_CONFIG: ConfigStatusDto = {
  provider: 'Replay',
  symbols: ['EURUSD', 'GBPUSD'],
  activeStyles: ['Intraday'],
  activeKillzones: ['LondonOpen', 'NewYorkOpen'],
  baseRiskPercent: 1,
  maxOpenPortfolioRiskPercent: 5,
  spreadBasePips: 0.7,
  commissionPerLotRoundTripUsd: 6,
  startingEquity: 10000,
};

// The live settings snapshot: one baked per-instrument override (NAS100 → a relaxed 7-concept subset, the
// tuned result) + the read-only global concept settings mirroring the §2.5.3/§5.1/§5.4 defaults. The mock PUT
// handler mutates the nested `instrumentOverrides` object IN PLACE (never reassigns the const), so the offline
// app behaves like the live one.
export const MOCK_SETTINGS: SettingsDto = {
  instrumentOverrides: {
    NAS100USD: {
      minRequiredConditions: null,
      requiredConditions: [
        'BiasAligned', 'KillzoneEntry', 'LiquiditySweep', 'DisplacementMss',
        'PremiumDiscountHalf', 'DrawTargetRrMet', 'CalendarClear',
      ],
      minStopDistancePips: null,
      spreadBasePips: null,
      commissionPerLotRoundTripUsd: null,
    },
  },
  global: {
    requiredConditions: [
      'BiasAligned', 'KillzoneEntry', 'LiquiditySweep', 'DisplacementMss',
      'FvgPresent', 'PremiumDiscountHalf', 'DrawTargetRrMet', 'CalendarClear',
    ],
    minRequiredConditions: null,
    weights: {
      KillzoneEntry: 1.0, LiquiditySweep: 0.95, DisplacementMss: 0.95, FvgPresent: 0.9,
      BiasAligned: 0.85, PremiumDiscountHalf: 0.85, OteZone: 0.7, OrderBlockConfluence: 0.65,
      DrawTargetRrMet: 0.65, SmtDivergence: 0.55, OpenPriceReference: 0.5, MacroTime: 0.45,
      CleanPriceAction: 0.4, CalendarDriver: 0.35,
    },
    gradeAThreshold: 80,
    gradeBThreshold: 65,
    gradeCThreshold: 50,
    alertMinimumGrade: 'B',
    baseRiskPercent: 1,
    maxOpenPortfolioRiskPercent: 5,
    hardMaxRiskPercent: 4.5,
    minStopDistancePips: 10,
    lossLadderPercents: [0.5, 0.25],
    consecutiveWinsForLowestUnit: 5,
    dipRecoveryFraction: 0.5,
    spreadBasePips: 0.7,
    commissionPerLotRoundTripUsd: 6,
    activeKillzones: ['LondonOpen', 'NewYorkOpen'],
    activeStyles: ['Intraday'],
  },
  availableRequiredConditions: [
    'BiasAligned', 'KillzoneEntry', 'LiquiditySweep', 'DisplacementMss',
    'FvgPresent', 'PremiumDiscountHalf', 'DrawTargetRrMet', 'CalendarClear',
  ],
  availableInstruments: [
    'AUDUSD', 'EURGBP', 'EURJPY', 'EURUSD', 'GBPJPY', 'GBPUSD',
    'NAS100USD', 'NZDUSD', 'USDCAD', 'USDCHF', 'USDJPY', 'XAUUSD',
  ],
};

export const MOCK_DATASETS: BacktestDatasetDto[] = [
  { symbol: 'EURUSD', timeframe: 'M5', fromUtc: '2023-10-01T00:00:00Z', toUtc: '2026-06-20T00:00:00Z', candleCount: 200000 },
  { symbol: 'GBPUSD', timeframe: 'M5', fromUtc: '2023-10-01T00:00:00Z', toUtc: '2026-06-20T00:00:00Z', candleCount: 200000 },
  { symbol: 'NAS100USD', timeframe: 'M5', fromUtc: '2023-10-01T00:00:00Z', toUtc: '2026-06-20T00:00:00Z', candleCount: 198450 },
];

/** A deterministic balance/cumulative-R curve for a mock backtest run. */
function mockBacktestEquity(startingBalance: number): BacktestResponse['equity'] {
  const rs = [0, 2.5, 1.5, 1.6, 0.6, 2.4, 1.4, 1.4, 0.95, 2.95, 1.95, 1.97];
  let balance = startingBalance;
  let cumR = 0;
  const riskAmt = startingBalance * 0.01;
  return rs.map((r, i) => {
    cumR += r;
    balance += r * riskAmt;
    return {
      atUtc: new Date(Date.parse('2026-01-02T14:00:00Z') + i * 6 * 3600 * 1000).toISOString(),
      equity: Number(balance.toFixed(2)),
      cumulativeR: Number(cumR.toFixed(2)),
    };
  });
}

/** Deterministic mock backtest run (POST /api/backtest). The trades reuse the closed-trade fixtures. */
export function mockBacktestResponse(
  symbol: string,
  style: string,
  startingBalance: number,
  riskPercent: number,
  timeframe = 'M5',
): BacktestResponse {
  const trades = MOCK_CLOSED_TRADES.map((tr) => ({ ...tr, symbol, style }));
  const equity = mockBacktestEquity(startingBalance);
  const endingBalance = equity.at(-1)?.equity ?? startingBalance;
  const wins = trades.filter((tr) => (tr.realizedR ?? 0) > 0).length;
  return {
    symbol,
    timeframe,
    style,
    fromUtc: '2026-01-02T00:00:00Z',
    toUtc: '2026-03-31T00:00:00Z',
    startingBalance,
    riskPercent,
    endingBalance,
    candlesProcessed: 24500,
    setupCount: 12,
    tradeCount: trades.length,
    summary: {
      tradeCount: trades.length,
      winRate: wins / trades.length,
      averageR: 0.71,
      profitFactor: 1.59,
      expectancy: 0.71,
      maxDrawdown: 2.0,
    },
    equity,
    trades,
  };
}

export const MOCK_BACKTEST: BacktestResponse = mockBacktestResponse('EURUSD', 'Intraday', 10000, 1);

/** Deterministic mock optimizer leaderboard (POST /api/backtest/optimize). */
export function mockOptimizeResponse(
  symbols: string[],
  styles: string[],
  riskPercents: number[],
  startingBalance: number,
  objective = 'Expectancy',
  topN = 10,
  timeframes: string[] = ['M5'],
): OptimizeResponse {
  const rows = [];
  for (const symbol of symbols.length ? symbols : ['EURUSD']) {
    for (const tf of timeframes.length ? timeframes : ['M5']) {
      for (const style of styles.length ? styles : ['Intraday']) {
        for (const risk of riskPercents.length ? riskPercents : [1]) {
          // Deterministic pseudo-score from the combination so the leaderboard ranks stably.
          const seed = (symbol.charCodeAt(0) + style.charCodeAt(0) + tf.charCodeAt(1) + risk * 10) % 23;
          const averageR = Number((0.05 + seed * 0.04).toFixed(3));
          const winRate = Number((0.4 + (seed % 7) * 0.03).toFixed(3));
          const profitFactor = Number((0.8 + seed * 0.07).toFixed(2));
          const tradeCount = 8 + (seed % 12);
          const expectancy = averageR;
          const endingBalance = Number((startingBalance * (1 + averageR * tradeCount * (risk / 100))).toFixed(2));
          const score =
            objective === 'ProfitFactor'
              ? profitFactor
              : objective === 'AverageR'
                ? averageR
                : objective === 'EndingBalance'
                  ? endingBalance
                  : expectancy;
          rows.push({
            symbol,
            timeframe: tf,
            style,
            riskPercent: risk,
            tradeCount,
            winRate,
            averageR,
            profitFactor,
            expectancy,
            maxDrawdownR: Number((1 + (seed % 5) * 0.5).toFixed(2)),
            endingBalance,
            score: Number(score.toFixed(4)),
          });
        }
      }
    }
  }
  rows.sort((a, b) => b.score - a.score);
  return {
    combinationCount: rows.length,
    objective,
    results: rows.slice(0, topN),
  };
}

export const MOCK_OPTIMIZE: OptimizeResponse = mockOptimizeResponse(
  ['EURUSD', 'GBPUSD'],
  ['Scalp', 'Intraday'],
  [0.5, 1, 1.5],
  10000,
);

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
