// ---------------------------------------------------------------------------------------------------
// MarketStatus (Live page, plan §9) — a compact card surfacing the live NY-session clock state from
// `GET /api/market-status`: a green OPEN / red CLOSED indicator (marketOpen), the current §2.1 session
// (highlighted when it is an operator-selected active killzone), and the NEXT session with a live
// countdown ("LondonOpen opens in 9h 00m"), plus the NY time. Dark-desk theme + KillzoneBadge palette +
// tabular numerals. Read-only — a status widget never places an order (plan §6.3).
// ---------------------------------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import type { Killzone, MarketStatusDto } from '../types/api';
import { errorMessage } from '../format-error';
import { KillzoneBadge } from './Badges';

export interface MarketStatusProps {
  status: MarketStatusDto | undefined;
  /** When this query last resolved (epoch ms) — anchors the local countdown interpolation between polls. */
  fetchedAt: number | undefined;
  isLoading: boolean;
  isError?: boolean;
  error?: unknown;
}

/** Minutes → "Xh Ym" (e.g. 540 → "9h 00m", 45 → "45m", 0 → "now"). Negative clamps to "now". */
function formatCountdown(minutes: number): string {
  const m = Math.max(0, Math.round(minutes));
  if (m === 0) return 'now';
  const h = Math.floor(m / 60);
  const mm = m % 60;
  if (h === 0) return `${mm}m`;
  return `${h}h ${String(mm).padStart(2, '0')}m`;
}

export function MarketStatus({
  status,
  fetchedAt,
  isLoading,
  isError,
  error,
}: MarketStatusProps): React.JSX.Element {
  // Tick once a second so the countdown decrements smoothly between the 30s polls (the DTO snapshots the
  // minutes-remaining at fetch time; we subtract the elapsed wall-clock since `fetchedAt` to interpolate).
  const [nowMs, setNowMs] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNowMs(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);

  const session = (status?.currentSession ?? 'None') as Killzone;
  const isActiveSession = Boolean(status?.inActiveKillzone) && session !== 'None';

  // Interpolated minutes until the next session: the snapshot minus the wall-clock elapsed since the poll.
  const remainingMinutes =
    status?.nextSessionOpensInMinutes != null && fetchedAt != null
      ? status.nextSessionOpensInMinutes - (nowMs - fetchedAt) / 60000
      : status?.nextSessionOpensInMinutes ?? null;

  return (
    <section className="panel market-status" aria-label="Market status">
      <header className="panel__head">
        <span>Market Status</span>
        {status ? <span className="app__clock num">{status.nowNy} NY</span> : null}
      </header>
      <div className="panel__body">
        {isError ? (
          <p className="empty error" role="alert">
            Market status unavailable — {errorMessage(error)}
          </p>
        ) : isLoading || !status ? (
          <p className="empty">Loading market status…</p>
        ) : (
          <div className="mkt">
            {/* Open / Closed indicator */}
            <div className="mkt__state">
              <span
                className={`mkt__dot ${status.marketOpen ? 'mkt__dot--open' : 'mkt__dot--closed'}`}
                aria-hidden="true"
              />
              <span className={`mkt__label ${status.marketOpen ? 'long' : 'short'}`}>
                {status.marketOpen ? 'OPEN' : 'CLOSED'}
              </span>
              <span className="mkt__day num">{status.dayOfWeekNy}</span>
            </div>

            {/* Current session — highlighted when it is an active killzone */}
            <div className={`mkt__session${isActiveSession ? ' mkt__session--active' : ''}`}>
              <span className="metric__label">Session</span>
              <span className="mkt__session-value">
                {session !== 'None' ? (
                  <KillzoneBadge killzone={session} />
                ) : (
                  <span className="neutral">No active session</span>
                )}
                {isActiveSession ? <span className="mkt__live">hunting</span> : null}
              </span>
            </div>

            {/* Next session + live countdown */}
            {status.nextSession && remainingMinutes != null ? (
              <div className="mkt__next">
                <span className="metric__label">Next</span>
                <span className="mkt__next-value">
                  <KillzoneBadge killzone={status.nextSession as Killzone} />
                  <span className="neutral">opens in</span>
                  <span className="num mkt__countdown">{formatCountdown(remainingMinutes)}</span>
                  {status.nextSessionStartsNy ? (
                    <span className="mkt__next-at num">· {status.nextSessionStartsNy} NY</span>
                  ) : null}
                </span>
              </div>
            ) : null}

            {/* The operator's active hunt-set */}
            {status.activeKillzones.length > 0 ? (
              <div className="mkt__kzs">
                <span className="metric__label">Hunting</span>
                <span className="chip-row">
                  {status.activeKillzones.map((k) => (
                    <KillzoneBadge key={k} killzone={k as Killzone} />
                  ))}
                </span>
              </div>
            ) : null}
          </div>
        )}
      </div>
    </section>
  );
}
