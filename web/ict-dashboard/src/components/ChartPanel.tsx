// ---------------------------------------------------------------------------------------------------
// ChartPanel (center, plan §9 / §9.1) — the ICT Pattern Chart plus its header controls (style filter +
// symbol/timeframe switcher) and the overlay legend (each §9.1 overlay toggles individually). The chart
// itself is IctChart (lightweight-charts). Read-only: the header carries no order/execute control.
// ---------------------------------------------------------------------------------------------------

import type { CandleDto, Killzone, TradeStyle } from '../types/api';
import {
  ALL_OVERLAY_KINDS,
  OVERLAY_LABELS,
  type ChartOverlay,
  type OverlayKind,
  type OverlayVisibility,
} from '../types/overlays';
import { IctChart } from '../chart/IctChart';
import { errorMessage } from '../format-error';
import { KillzoneBadge, StyleChip } from './Badges';

// Every instrument the live OANDA feed streams + the InstrumentCatalog can size (10 FX majors + 3 index
// CFDs + gold). The feed delivers all of these on all timeframes; the chart can render any (symbol, tf).
export const SYMBOLS = [
  'EURUSD', 'GBPUSD', 'USDJPY', 'AUDUSD', 'USDCHF', 'USDCAD', 'NZDUSD', 'EURGBP', 'EURJPY', 'GBPJPY',
  'NAS100USD', 'SPX500USD', 'US30USD', 'XAUUSD',
] as const;
// All feed-streamed granularities the chart can show. M1/M5/M15/H4 are the scanned style entry-TFs;
// M30/H1/D1 are charted-but-not-scanned (no style maps to them) — they enrich the chart only.
export const TIMEFRAMES = ['M1', 'M5', 'M15', 'M30', 'H1', 'H4', 'D1'] as const;
export const STYLES: readonly TradeStyle[] = ['Scalp', 'Intraday', 'Swing', 'Position'];

/** Friendly display labels for symbols whose wire ticker is not the natural name (e.g. the index CFDs). */
const SYMBOL_LABELS: Readonly<Record<string, string>> = {
  NAS100USD: 'NAS100',
  SPX500USD: 'SPX500',
  US30USD: 'US30',
  XAUUSD: 'Gold',
};

/** The label to show in the symbol selector — the friendly name when one exists, else the raw ticker. Kept
 *  non-exported so this component file does not add a Fast-Refresh non-component export (lint stays clean). */
function symbolLabel(symbol: string): string {
  return SYMBOL_LABELS[symbol] ?? symbol;
}

const OVERLAY_SWATCH: Record<OverlayKind, string> = {
  killzone: 'var(--pending)',
  liquidity: 'var(--short)',
  sweep: 'var(--pending)',
  mss: 'var(--accent)',
  fvg: 'var(--long)',
  orderBlock: 'var(--entry)',
  ote: '#c792ea',
  tradeLevels: 'var(--entry)',
  drawOnLiquidity: 'var(--pending)',
};

export interface ChartPanelProps {
  symbol: string;
  timeframe: string;
  style: TradeStyle;
  candles: CandleDto[];
  overlays: ChartOverlay[];
  visibility: OverlayVisibility;
  isLoading: boolean;
  isError?: boolean;
  error?: unknown;
  /** Optional UTC instant to seek the chart to (focus-on-alert/trade). */
  seekToUtc?: string;
  activeKillzone: Killzone | null;
  triggerTimeframe: string | null;
  onSymbolChange: (symbol: string) => void;
  onTimeframeChange: (timeframe: string) => void;
  onStyleChange: (style: TradeStyle) => void;
  onToggleOverlay: (kind: OverlayKind) => void;
}

export function ChartPanel(props: ChartPanelProps): React.JSX.Element {
  const {
    symbol,
    timeframe,
    style,
    candles,
    overlays,
    visibility,
    isLoading,
    isError,
    error,
    seekToUtc,
    activeKillzone,
    triggerTimeframe,
    onSymbolChange,
    onTimeframeChange,
    onStyleChange,
    onToggleOverlay,
  } = props;

  return (
    <section className="panel layout__chart" aria-label="ICT pattern chart">
      <header className="panel__head" style={{ textTransform: 'none' }}>
        <div className="chart-controls">
          <select
            className="input"
            value={symbol}
            aria-label="Symbol"
            onChange={(e) => onSymbolChange(e.target.value)}
          >
            {SYMBOLS.map((s) => (
              <option key={s} value={s}>
                {symbolLabel(s)}
              </option>
            ))}
          </select>

          <div className="seg" role="group" aria-label="Timeframe">
            {TIMEFRAMES.map((tf) => (
              <button
                key={tf}
                type="button"
                aria-pressed={tf === timeframe}
                onClick={() => onTimeframeChange(tf)}
              >
                {tf}
              </button>
            ))}
          </div>

          <div className="seg" role="group" aria-label="Trade style filter">
            {STYLES.map((s) => (
              <button key={s} type="button" aria-pressed={s === style} onClick={() => onStyleChange(s)}>
                {s}
              </button>
            ))}
          </div>
        </div>

        <div className="chart-controls">
          <StyleChip style={style} />
          {triggerTimeframe ? <span className="chip chip--style num">{triggerTimeframe}</span> : null}
          <KillzoneBadge killzone={activeKillzone} />
        </div>
      </header>

      {isError ? (
        <div className="chart-surface">
          <p className="empty error" role="alert">
            Chart unavailable — {errorMessage(error)}
          </p>
        </div>
      ) : isLoading ? (
        <div className="chart-surface">
          <p className="empty">Loading candles…</p>
        </div>
      ) : (
        <IctChart
          candles={candles}
          overlays={overlays}
          visibility={visibility}
          seekToUtc={seekToUtc}
        />
      )}

      <div className="chart-legend" role="group" aria-label="Overlay toggles">
        {ALL_OVERLAY_KINDS.map((kind) => (
          <button
            key={kind}
            type="button"
            className="legend-toggle"
            aria-pressed={visibility[kind]}
            onClick={() => onToggleOverlay(kind)}
          >
            <span className="legend-swatch" style={{ background: OVERLAY_SWATCH[kind] }} />
            {OVERLAY_LABELS[kind]}
          </button>
        ))}
      </div>
    </section>
  );
}
