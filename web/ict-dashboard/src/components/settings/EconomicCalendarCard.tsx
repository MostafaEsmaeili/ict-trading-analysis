// ---------------------------------------------------------------------------------------------------
// EconomicCalendarCard — the economic-calendar status (§2.5.8): the source/loaded state, the upcoming
// FOMC/NFP/CPI events, and the §2.5.2 no-trade days the gate enforces (extracted unchanged from the
// monolithic SettingsPage). Read-only — it shows what the scanner BLOCKS, with no order/execute control.
// ---------------------------------------------------------------------------------------------------

import { useCalendar } from '../../api/hooks';
import type { CalendarStatusDto } from '../../types/api';
import { errorMessage } from '../../format-error';

export function EconomicCalendarCard(): React.JSX.Element {
  const calendarQ = useCalendar();
  const calendar: CalendarStatusDto | undefined = calendarQ.data;

  return (
    <section className="panel" aria-label="Economic calendar">
      <header className="panel__head">
        <span>Economic calendar</span>
        {calendar ? (
          <span className="num neutral">
            {calendar.enabled ? `${calendar.provider} · ${calendar.loaded ? 'loaded' : 'not loaded'}` : 'disabled'}
          </span>
        ) : null}
      </header>
      <div className="panel__body">
        {calendarQ.isError ? (
          <p className="empty error" role="alert">
            Calendar unavailable — {errorMessage(calendarQ.error)}
          </p>
        ) : !calendar ? (
          <p className="empty">{calendarQ.isLoading ? 'Loading…' : 'No calendar available.'}</p>
        ) : !calendar.enabled ? (
          <p className="empty">
            The calendar feed is disabled (<code>Ict:Calendar:Enabled=false</code>), so the §2.5.2 FOMC/NFP gate is
            fail-open — no day is blocked. Enable a source to enforce the no-trade days.
          </p>
        ) : (
          <>
            <div className="cfg__row">
              <span className="cfg__label">Window (NY)</span>
              <span className="cfg__value">
                {calendar.fromDate} → {calendar.toDate} · {calendar.blackoutDates.length} no-trade day(s)
              </span>
            </div>
            {calendar.events.length === 0 ? (
              <p className="empty">No FOMC/NFP/CPI events in the window.</p>
            ) : (
              <table className="tbl" aria-label="Economic events" style={{ marginTop: 8 }}>
                <thead>
                  <tr>
                    <th>Date (NY)</th>
                    <th>Event</th>
                    <th>No-trade</th>
                  </tr>
                </thead>
                <tbody>
                  {calendar.events.map((e) => (
                    <tr key={`${e.date}-${e.type}`}>
                      <td className="num">{e.date}</td>
                      <td>{e.type}</td>
                      <td className={`num ${e.isBlackout ? 'short' : 'long'}`}>{e.isBlackout ? 'blocked' : 'clear'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}
      </div>
    </section>
  );
}
