// ---------------------------------------------------------------------------------------------------
// Alerts Feed (left panel, plan §9) — newest-first list of advisory setup alerts: reasoning string +
// killzone badge + direction + style chip. Clicking an alert focuses the chart on that symbol (the
// focus callback is wired by the dashboard). Read-only: there are no act/execute controls.
// ---------------------------------------------------------------------------------------------------

import type { AlertDto } from '../types/api';
import { formatNyDateTime } from '../time';
import { DirectionChip, KillzoneBadge, StyleChip } from './Badges';

export interface AlertsFeedProps {
  alerts: AlertDto[];
  isLoading: boolean;
  onFocusSymbol?: (symbol: string) => void;
}

export function AlertsFeed({ alerts, isLoading, onFocusSymbol }: AlertsFeedProps): React.JSX.Element {
  return (
    <section className="panel layout__alerts" aria-label="Alerts feed">
      <header className="panel__head">
        <span>Alerts</span>
        <span className="neutral num">{alerts.length}</span>
      </header>
      <div className="panel__body panel__body--flush">
        {isLoading ? (
          <p className="empty">Loading alerts…</p>
        ) : alerts.length === 0 ? (
          <p className="empty">No setups detected yet.</p>
        ) : (
          alerts.map((a) => (
            <button
              key={a.id}
              type="button"
              className="alert"
              onClick={() => onFocusSymbol?.(a.symbol)}
              aria-label={`Focus chart on ${a.symbol}`}
            >
              <div className="alert__top">
                <span className="alert__symbol">{a.symbol}</span>
                <DirectionChip direction={a.direction} />
                <KillzoneBadge killzone={a.killzone} />
                <StyleChip style={a.style} />
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
