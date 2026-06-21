// ---------------------------------------------------------------------------------------------------
// ChartPanel (center, plan §9 / §9.1) — the ICT Pattern Chart plus its header controls (style filter +
// symbol/timeframe switcher) and the overlay legend (each §9.1 overlay toggles individually). The chart
// itself is IctChart (lightweight-charts). Read-only: the header carries no order/execute control.
// ---------------------------------------------------------------------------------------------------

import type { TradeStyle } from '../types/api';
import type { CandleDto } from '../types/api';
import {
  ALL_OVERLAY_KINDS,
  OVERLAY_LABELS,
  type ChartOverlay,
  type OverlayKind,
  type OverlayVisibility,
} from '../types/overlays';
import { IctChart } from '../chart/IctChart';
import { KillzoneBadge, StyleChip } from './Badges';

export const SYMBOLS = ['EURUSD', 'GBPUSD', 'USDJPY', 'XAUUSD'] as const;
export const TIMEFRAMES = ['M1', 'M5', 'M15', 'H1'] as const;
export const STYLES: readonly TradeStyle[] = ['Scalp', 'Intraday', 'Swing', 'Position'];

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
  activeKillzone: string | null;
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
                {s}
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

      {isLoading ? (
        <div className="chart-surface">
          <p className="empty">Loading candles…</p>
        </div>
      ) : (
        <IctChart candles={candles} overlays={overlays} visibility={visibility} />
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
