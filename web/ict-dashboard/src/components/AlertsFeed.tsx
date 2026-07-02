// ---------------------------------------------------------------------------------------------------
// Alerts Feed (left panel, plan §9) — newest-first list of advisory setup alerts: reasoning string +
// killzone badge + direction + style chip. Clicking an alert focuses the chart on that symbol (the
// focus callback is wired by the dashboard). Read-only: there are no act/execute controls.
//
// Each row is now DISMISSIBLE (per-row ✕) and the header has a "Clear all" — dismissed ids persist in
// localStorage (useDismissedAlerts) so the append-only feed can be tidied and stays tidy across a
// refresh (part of the "notifications can't be closed" complaint). The ✕ stops propagation so it never
// triggers the row's focus-chart click. DEFENSIVE: the ERROR branch keeps NO dismiss control — a failed
// query is the defensive signal and must stay visible (§6.3); only successful alert rows are dismissible.
// ---------------------------------------------------------------------------------------------------

import { useMemo } from 'react';
import type { AlertDto, Direction, Killzone, TradeStyle } from '../types/api';
import { formatNyDateTime } from '../time';
import { errorMessage } from '../format-error';
import { useDismissedAlerts } from '../hooks/useDismissedAlerts';
import { DirectionChip, KillzoneBadge, ModelBadge, StyleChip } from './Badges';

/**
 * Focus target — the symbol to switch to, plus an optional instant to seek the chart to. Alerts/trades
 * carry no timeframe/style, so those are optional; a signal (whose DTO carries them) supplies them too so
 * the chart can switch the TF + style as well, not just the symbol.
 */
export interface FocusTarget {
  symbol: string;
  atUtc?: string;
  timeframe?: string;
  style?: TradeStyle;
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
  const { isDismissed, dismiss, dismissMany } = useDismissedAlerts();

  // The rows still on screen after local dismissals (display-only; host data is untouched).
  const visible = useMemo(() => alerts.filter((a) => !isDismissed(a.id)), [alerts, isDismissed]);

  return (
    <section className="panel layout__alerts" aria-label="Alerts feed">
      <header className="panel__head">
        <span>Alerts</span>
        <span className="panel__head-right">
          <span className="neutral num">{visible.length}</span>
          {!isError && !isLoading && visible.length > 0 ? (
            <button
              type="button"
              className="notice-link"
              onClick={() => dismissMany(visible.map((a) => a.id))}
            >
              Clear all
            </button>
          ) : null}
        </span>
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
        ) : visible.length === 0 ? (
          <p className="empty">All caught up.</p>
        ) : (
          visible.map((a) => (
            <div key={a.id} className="alert-row">
              <button
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
                  <ModelBadge model={a.model} />
                  <span className="alert__time num">{formatNyDateTime(a.atUtc)} NY</span>
                </div>
                <p className="alert__msg">{a.message}</p>
              </button>
              <button
                type="button"
                className="alert__close"
                aria-label="Dismiss alert"
                onClick={(e) => {
                  // Don't let the dismiss bubble to the row's focus-chart click.
                  e.stopPropagation();
                  dismiss(a.id);
                }}
              >
                ✕
              </button>
            </div>
          ))
        )}
      </div>
    </section>
  );
}
