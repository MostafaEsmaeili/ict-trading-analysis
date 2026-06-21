// ---------------------------------------------------------------------------------------------------
// Chart overlay geometry (plan §9.1). FRONTEND-INTERNAL — not a backend contract.
//
// In production the geometry is derived from a confirmed Setup's `Evidence` JSONB (zone bounds, levels,
// timestamps) returned by `GET /api/chart/{symbol}?tf=&style=` and streamed via `SetupDetected`. For this
// scaffold the geometry comes from a deterministic mock fixture (src/mocks). Times are ISO-8601 UTC.
// ---------------------------------------------------------------------------------------------------

import type { Direction, Killzone } from './api';

/** Every overlay carries a `kind` discriminator (the §9.1 concept table) and a per-kind toggle group. */
export type OverlayKind =
  | 'fvg'
  | 'orderBlock'
  | 'liquidity'
  | 'sweep'
  | 'mss'
  | 'ote'
  | 'killzone'
  | 'tradeLevels'
  | 'drawOnLiquidity';

/** Fair Value Gap — translucent rectangle across the gap; greys out when mitigated (two-touch). */
export interface FvgOverlay {
  kind: 'fvg';
  direction: Direction;
  fromUtc: string;
  toUtc: string;
  top: number;
  bottom: number;
  mitigated: boolean;
}

/** Order block / breaker — bordered rectangle (open→range) + a 50% mean-threshold line. */
export interface OrderBlockOverlay {
  kind: 'orderBlock';
  direction: Direction;
  isBreaker: boolean;
  fromUtc: string;
  toUtc: string;
  top: number;
  bottom: number;
  meanThreshold: number;
}

/** Liquidity pool / equal highs-lows — dashed horizontal line with a buy-side/sell-side tag. */
export interface LiquidityOverlay {
  kind: 'liquidity';
  side: 'buy' | 'sell';
  price: number;
  fromUtc: string;
  label: string;
  swept: boolean;
}

/** Liquidity sweep (Judas) — triangle marker on the wick that raided the level. */
export interface SweepOverlay {
  kind: 'sweep';
  direction: Direction;
  atUtc: string;
  price: number;
}

/** MSS / displacement — arrow marker + a line at the broken swing. */
export interface MssOverlay {
  kind: 'mss';
  direction: Direction;
  atUtc: string;
  brokenSwingPrice: number;
}

/** OTE zone — shaded 62–79% band + the 70.5% sweet-spot line. */
export interface OteOverlay {
  kind: 'ote';
  direction: Direction;
  fromUtc: string;
  toUtc: string;
  band62: number;
  band79: number;
  sweetSpot705: number;
}

/** Killzone — vertical background band (Asian indigo / London teal / NY orange / PM amber). */
export interface KillzoneOverlay {
  kind: 'killzone';
  killzone: Killzone;
  fromUtc: string;
  toUtc: string;
}

/** Entry / Stop / Targets — price lines (entry blue, stop red, T1/T2 green) with R labels. */
export interface TradeLevelsOverlay {
  kind: 'tradeLevels';
  direction: Direction;
  entry: number;
  stop: number;
  targets: number[];
  rewardRatio: number;
}

/** Daily bias / draw-on-liquidity — an HTF level line + arrow pointing at the targeted pool. */
export interface DrawOnLiquidityOverlay {
  kind: 'drawOnLiquidity';
  direction: Direction;
  fromUtc: string;
  targetPrice: number;
}

export type ChartOverlay =
  | FvgOverlay
  | OrderBlockOverlay
  | LiquidityOverlay
  | SweepOverlay
  | MssOverlay
  | OteOverlay
  | KillzoneOverlay
  | TradeLevelsOverlay
  | DrawOnLiquidityOverlay;

/** Per-kind visibility state for the chart legend (every overlay toggles individually — §9.1). */
export type OverlayVisibility = Record<OverlayKind, boolean>;

export const ALL_OVERLAY_KINDS: readonly OverlayKind[] = [
  'killzone',
  'liquidity',
  'sweep',
  'mss',
  'fvg',
  'orderBlock',
  'ote',
  'tradeLevels',
  'drawOnLiquidity',
];

export const OVERLAY_LABELS: Record<OverlayKind, string> = {
  killzone: 'Killzone',
  liquidity: 'Liquidity',
  sweep: 'Sweep',
  mss: 'MSS / Displacement',
  fvg: 'Fair Value Gap',
  orderBlock: 'Order Block',
  ote: 'OTE 62–79%',
  tradeLevels: 'Entry / Stop / Targets',
  drawOnLiquidity: 'Draw on Liquidity',
};

export function defaultOverlayVisibility(): OverlayVisibility {
  return {
    killzone: true,
    liquidity: true,
    sweep: true,
    mss: true,
    fvg: true,
    orderBlock: true,
    ote: true,
    tradeLevels: true,
    drawOnLiquidity: true,
  };
}
