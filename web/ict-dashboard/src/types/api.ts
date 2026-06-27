// ---------------------------------------------------------------------------------------------------
// Frozen backend contract mirror (contracts-v1 — plan §11.1 #1/#4/#6/#7).
//
// These types MUST stay byte-for-byte aligned with the C# Contracts DTOs under
// `src/Modules/*/Contracts/**` and `src/IctTrader.Host/Contracts/**`. JSON is camelCase
// (System.Text.Json default). DO NOT invent shapes — when the backend changes a DTO, regenerate
// `api.generated.ts` from the OpenAPI document (`npm run gen:api`) and reconcile here.
//
// Wire note: the C# DTOs type Direction / Killzone / Style / Grade / Status as `string` for a stable,
// language-neutral contract. We keep the DTO fields as `string` (faithful to the wire) and ALSO export
// the exact enum member-name unions below so the UI can switch on them type-safely.
// ---------------------------------------------------------------------------------------------------

// ---- Enum member-name unions (FROZEN — the exact C# enum names; plan §11.1 #1/#7) ----

/** IctTrader.Domain.ValueObjects.Direction — structure/bias/setup direction. */
export type Direction = 'Bullish' | 'Bearish';

/** IctTrader.Domain.ValueObjects.TradeDirection — the side of a paper trade (Gherkin asserts these). */
export type TradeDirection = 'Long' | 'Short';

/**
 * IctTrader.Domain.Sessions.Killzone. `None` = outside every killzone. `Pm`/`Am` are the WP1
 * instrument-class windows (NOT part of the operator-selectable active set).
 */
export type Killzone =
  | 'None'
  | 'Asian'
  | 'LondonOpen'
  | 'NewYorkOpen'
  | 'LondonClose'
  | 'Pm'
  | 'Am';

/** IctTrader.Domain.Styles.TradeStyle — backs the dashboard style filter + chart badge. Default Intraday. */
export type TradeStyle = 'Scalp' | 'Intraday' | 'Swing' | 'Position';

/** IctTrader.Domain.Setups.SetupGrade — alert gate. Only A & B fire (floor 65). */
export type SetupGrade = 'Reject' | 'C' | 'B' | 'A';

/** IctTrader.Domain.Trading.TradeStatus — the account/ledger flag (Open until the final close). */
export type TradeStatus = 'Open' | 'Closed';

/** IctTrader.Domain.Trading.TradeCloseReason. */
export type TradeCloseReason = 'TargetHit' | 'StopHit' | 'TimeExit' | 'Manual';

// ---- MarketData.Contracts ----

/** Mirrors MarketData.Contracts.CandleDto. `openTimeUtc` is an ISO-8601 UTC instant. */
export interface CandleDto {
  symbol: string;
  timeframe: string;
  openTimeUtc: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

/** Mirrors MarketData.Contracts.TickDto. */
export interface TickDto {
  symbol: string;
  timeUtc: string;
  bid: number;
  ask: number;
}

/** Mirrors MarketData.Contracts.FeedStatusDto. `isReadOnly` is always true (plan §6.3). */
export interface FeedStatusDto {
  provider: string;
  connected: boolean;
  isReadOnly: boolean;
}

// ---- Scanning.Contracts ----

/**
 * Mirrors Scanning.Contracts.SetupDto — a confirmed, ADVISORY setup. `isAdvisoryOnly` is always true
 * (plan §6.3). `direction` / `killzone` / `style` / `grade` carry the frozen enum member names above.
 */
export interface SetupDto {
  id: string;
  symbol: string;
  direction: string;
  killzone: string;
  style: string;
  grade: string;
  triggerTimeframe: string;
  entry: number;
  stop: number;
  targets: number[];
  rewardRatio: number;
  reason: string;
  detectedAtUtc: string;
  isAdvisoryOnly: boolean;
}

/** Mirrors Scanning.Contracts.ScanStatusDto. */
export interface ScanStatusDto {
  symbol: string;
  activeKillzone: string | null;
  openSetups: number;
}

// ---- PaperTrading.Contracts ----

/** IctTrader.Domain.Trading.TradeLifecycle — Open → PartialTaken (a scale-out booked) → Closed. */
export type TradeLifecycle = 'Open' | 'PartialTaken' | 'Closed';

/**
 * Mirrors PaperTrading.Contracts.PaperTradeDto — a SIMULATED trade. There is no live counterpart
 * anywhere in the system (plan §6.3). `realizedR` is null while the trade is open.
 *
 * EXTENDED (plan §15): the full trade record the Trades-history + Backtest tables render — lifecycle,
 * close reason, net P&L breakdown (gross / costs / net), the live `currentStop` (ratcheted by the
 * trail), the scale-out / breakeven flags, the reserved risk budget and the trigger timeframe. All
 * fields are still advisory paper; `direction` carries Long/Short (TradeDirection).
 */
export interface PaperTradeDto {
  id: string;
  setupId: string;
  symbol: string;
  direction: string;
  status: string;
  style: string;
  killzone: string | null;
  entry: number;
  stop: number;
  targets: number[];
  size: number;
  openedAtUtc: string;
  closedAtUtc: string | null;
  realizedR: number | null;
  lifecycle: string;
  closeReason: string | null;
  netR: number | null;
  grossPnl: number | null;
  costs: number | null;
  netPnl: number | null;
  hasScaledOut: boolean;
  isBreakevenArmed: boolean;
  riskBudget: number;
  timeframe: string;
  currentStop: number;
  exitPrice: number | null;
  managedFromUtc: string;
}

/**
 * Mirrors PaperTrading.Contracts.AccountStatusDto — the live paper account snapshot (plan §15 §3).
 * Money is in account currency; `openRisk`/`openRiskCap` are reserved-risk amounts; the
 * `riskUtilizationPercent` is `openRisk / openRiskCap` as a 0..100 percent. Streaks feed the §2.4
 * adaptive risk ladder. Read-only — there is no deposit/withdraw control anywhere (§6.3).
 */
export interface AccountStatusDto {
  startingEquity: number;
  equity: number;
  peakEquity: number;
  drawdownTrough: number;
  openRisk: number;
  openRiskCap: number;
  riskUtilizationPercent: number;
  maxOpenPortfolioRiskPercent: number;
  consecutiveWins: number;
  consecutiveLosses: number;
  openTradeCount: number;
}

/**
 * Mirrors Host.ConfigStatusDto — the operator-visible runtime configuration (plan §15 §3). Reflects
 * the bound `Ict:*` options: the data provider, the scanned symbols, the active styles/killzones, the
 * risk %, the §5.4 cost model (spread + commission) and the starting equity.
 */
export interface ConfigStatusDto {
  provider: string;
  symbols: string[];
  activeStyles: string[];
  activeKillzones: string[];
  baseRiskPercent: number;
  maxOpenPortfolioRiskPercent: number;
  spreadBasePips: number;
  commissionPerLotRoundTripUsd: number;
  startingEquity: number;
}

// ---- Settings (plan §15 — live per-instrument tuning + concept-settings view) ----

/**
 * Mirrors Host.InstrumentSettingsDto — one symbol's LIVE override. Every field is optional; a null/omitted
 * field inherits the built-in catalog default. `requiredConditions` is the subset (by member name) to
 * require; `minRequiredConditions` is the k-of-n count.
 */
export interface InstrumentSettingsDto {
  minRequiredConditions?: number | null;
  requiredConditions?: string[] | null;
  minStopDistancePips?: number | null;
  spreadBasePips?: number | null;
  commissionPerLotRoundTripUsd?: number | null;
}

/**
 * Mirrors Host.GlobalConceptSettingsDto — the global ICT concept settings the scanner runs under, projected
 * READ-ONLY (bound from `Ict:*` at startup). The live-editable surface is the per-instrument override.
 */
export interface GlobalConceptSettingsDto {
  requiredConditions: string[];
  minRequiredConditions: number | null;
  weights: Record<string, number>;
  gradeAThreshold: number;
  gradeBThreshold: number;
  gradeCThreshold: number;
  alertMinimumGrade: string;
  baseRiskPercent: number;
  maxOpenPortfolioRiskPercent: number;
  hardMaxRiskPercent: number;
  minStopDistancePips: number;
  lossLadderPercents: number[];
  consecutiveWinsForLowestUnit: number;
  dipRecoveryFraction: number;
  spreadBasePips: number;
  commissionPerLotRoundTripUsd: number;
  activeKillzones: string[];
  activeStyles: string[];
}

/** Mirrors Host.SettingsDto — the live settings snapshot the Settings page reads. */
export interface SettingsDto {
  instrumentOverrides: Record<string, InstrumentSettingsDto>;
  global: GlobalConceptSettingsDto;
  /** The confluence conditions a per-instrument required-subset may be drawn from (the §2.5.2 canonical set). */
  availableRequiredConditions: string[];
  /** The catalogued symbols the operator can pick to add an override (FX majors + NAS100USD); typing another is allowed. */
  availableInstruments: string[];
}

// ---- Backtest / Optimizer (plan §15 §5/§6) ----

/** Mirrors Host.BacktestDatasetDto — a CSV history dataset available to the Backtest Lab. */
export interface BacktestDatasetDto {
  symbol: string;
  timeframe: string;
  fromUtc: string;
  toUtc: string;
  candleCount: number;
}

/** `POST /api/backtest` body — run the §2.5 model over a dataset slice (plan §15 §5). */
export interface BacktestRequest {
  symbol: string;
  style: string;
  startingBalance: number;
  riskPercent: number;
  timeframe?: string;
  fromUtc?: string;
  toUtc?: string;
  /** "k of n" required-condition relaxation; omit/undefined = strict all-required §2.5 model. */
  minRequiredConditions?: number;
  /** The specific concepts to require (the feature-subset); omit = the default/instrument required set. */
  requiredConditions?: string[];
}

/**
 * Mirrors Host.BacktestEquityPointDto — one point on the backtest balance curve. `equity` is the
 * ACCOUNT BALANCE (money), `cumulativeR` the running ΣR (so the curve can toggle units, plan §15 §5).
 */
export interface BacktestEquityPointDto {
  atUtc: string;
  equity: number;
  cumulativeR: number;
}

/** `POST /api/backtest` 200 response — the run summary, equity curve and the trades it produced. */
export interface BacktestResponse {
  symbol: string;
  timeframe: string;
  style: string;
  fromUtc: string;
  toUtc: string;
  startingBalance: number;
  riskPercent: number;
  minRequiredConditions?: number | null;
  requiredConditions?: string[] | null;
  endingBalance: number;
  candlesProcessed: number;
  setupCount: number;
  tradeCount: number;
  summary: PerformanceSummaryDto;
  equity: BacktestEquityPointDto[];
  trades: PaperTradeDto[];
}

/** `POST /api/backtest/optimize` body — a grid sweep over the cartesian product (plan §15 §6). */
export interface OptimizeRequest {
  symbols: string[];
  styles: string[];
  riskPercents: number[];
  startingBalance: number;
  timeframes?: string[];
  fromUtc?: string;
  toUtc?: string;
  objective?: string;
  topN: number;
  /** Sweep the "k of n" required-condition relaxation; omit = strict only (all required). */
  minRequiredConditions?: number[];
  /** Explicit candidate required-condition subsets to sweep (each a list of concept names). */
  requiredConditionSets?: string[][];
  /** Auto-generate subsets by dropping up to this many of the (non-MSS) default required conditions. */
  leaveOutUpTo?: number;
}

/** One row of the optimizer leaderboard — a single (symbol,tf,style,risk%) combination's result. */
export interface OptimizerResultDto {
  symbol: string;
  timeframe: string;
  style: string;
  riskPercent: number;
  minRequiredConditions?: number | null;
  requiredConditions?: string[] | null;
  tradeCount: number;
  winRate: number;
  averageR: number;
  profitFactor: number;
  expectancy: number;
  maxDrawdownR: number;
  endingBalance: number;
  score: number;
}

/** `POST /api/backtest/optimize` 200 response — the ranked leaderboard (plan §15 §6). */
export interface OptimizeResponse {
  combinationCount: number;
  objective: string;
  results: OptimizerResultDto[];
}

/** A 400/404 error body the backtest/optimize endpoints return on a bad/missing request. */
export interface ApiErrorDto {
  error: string;
}

// ---- Alerting.Contracts ----

/** Mirrors Alerting.Contracts.AlertDto. */
export interface AlertDto {
  id: string;
  kind: string;
  symbol: string;
  message: string;
  direction: string | null;
  killzone: string | null;
  style: string | null;
  atUtc: string;
}

// ---- Performance.Contracts ----

/**
 * Sentinel emitted by the backend `PerformanceCalculator.UndefinedProfitFactor` when there are no
 * losing trades (gross loss = 0), i.e. profit factor is mathematically undefined / infinite. The
 * dashboard renders this as "∞" rather than the raw magnitude. Mirrors the C# `decimal` constant.
 */
export const UNDEFINED_PROFIT_FACTOR = 999999;

/**
 * Mirrors Performance.Contracts.PerformanceSummaryDto.
 *
 * Unit notes (the wire is R-based, NOT dollars/percent — see PerformanceCalculator):
 * - `winRate` is a true 0..1 fraction (render via the `pct()` helper).
 * - `averageR` / `expectancy` are in R units.
 * - `maxDrawdown` is a POSITIVE absolute peak-to-trough magnitude in R units (never a percent).
 * - `profitFactor` uses the {@link UNDEFINED_PROFIT_FACTOR} sentinel for "no losses / undefined".
 */
export interface PerformanceSummaryDto {
  tradeCount: number;
  winRate: number;
  averageR: number;
  profitFactor: number;
  expectancy: number;
  maxDrawdown: number;
}

/**
 * Mirrors Performance.Contracts.EquityPointDto. `equity` is CUMULATIVE R from a zero baseline
 * (running ΣR), NOT an account-currency balance — the curve passes through ~0.
 */
export interface EquityPointDto {
  atUtc: string;
  equity: number;
}

// ---- Host.Contracts ----

/** Mirrors IctTrader.Host.ChartResponse — `GET /api/chart/{symbol}?tf=&style=` (candles + overlays). */
export interface ChartResponse {
  symbol: string;
  timeframe: string;
  style: string;
  candles: CandleDto[];
  overlays: SetupDto[];
}

/** Mirrors IctTrader.Host.ExecutePaperTradeRequest — advisory request to SIMULATE a trade (plan §6.3). */
export interface ExecutePaperTradeRequest {
  setupId: string;
}
