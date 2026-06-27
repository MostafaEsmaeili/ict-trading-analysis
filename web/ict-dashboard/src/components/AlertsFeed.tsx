// ---------------------------------------------------------------------------------------------------
// Alerts Feed (left panel, plan §9) — newest-first list of advisory setup alerts: reasoning string +
// killzone badge + direction + style chip. Clicking an alert focuses the chart on that symbol (the
// focus callback is wired by the dashboard). Read-only: there are no act/execute controls.
// ---------------------------------------------------------------------------------------------------

import type { AlertDto, Direction, Killzone, TradeStyle } from '../types/api';
import { formatNyDateTime } from '../time';
import { errorMessage } from '../format-error';
import { DirectionChip, KillzoneBadge, StyleChip } from './Badges';

/** Focus target — the symbol to switch to, plus an optional instant to seek the chart to. */
export interface FocusTarget {
  symbol: string;
  atUtc?: string;
}

export interface AlertsFeedProps {
  alerts: AlertDto[];
  isLoading: boolean;
  isError?: boolean;
  error?: unknown;
  onFocus?: (target: FocusTarget) => void;
}

export function AlertsFeed({
  alerts,
  isLoading,
  isError,
  error,
  onFocus,
}: AlertsFeedProps): React.JSX.Element {
  return (
    <section className="panel layout__alerts" aria-label="Alerts feed">
      <header className="panel__head">
        <span>Alerts</span>
        <span className="neutral num">{alerts.length}</span>
      </header>
      <div className="panel__body panel__body--flush">
        {isError ? (
          <p className="empty error" role="alert">
            Alerts unavailable — {errorMessage(error)}
          </p>
        ) : isLoading ? (
          <p className="empty">Loading alerts…</p>
        ) : alerts.length === 0 ? (
          <p className="empty">No setups detected yet.</p>
        ) : (
          alerts.map((a) => (
            <button
              key={a.id}
              type="button"
              className="alert"
              onClick={() => onFocus?.({ symbol: a.symbol, atUtc: a.atUtc })}
              aria-label={`Focus chart on ${a.symbol}`}
            >
              <div className="alert__top">
                <span className="alert__symbol">{a.symbol}</span>
                <DirectionChip direction={a.direction as Direction | null} />
                <KillzoneBadge killzone={a.killzone as Killzone | null} />
                <StyleChip style={a.style as TradeStyle | null} />
                <span className="alert__time num">{formatNyDateTime(a.atUtc)} NY</span>
              </div>
              <p className="alert__msg">{a.message}</p>
            </button>
          ))
        )}
      </div>
    </section>
  );
}
