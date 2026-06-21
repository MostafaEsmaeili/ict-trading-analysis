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

/**
 * Mirrors PaperTrading.Contracts.PaperTradeDto — a SIMULATED trade. There is no live counterpart
 * anywhere in the system (plan §6.3). `realizedR` is null while the trade is open.
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

/** Mirrors Performance.Contracts.PerformanceSummaryDto. */
export interface PerformanceSummaryDto {
  tradeCount: number;
  winRate: number;
  averageR: number;
  profitFactor: number;
  expectancy: number;
  maxDrawdown: number;
}

/** Mirrors Performance.Contracts.EquityPointDto. */
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
