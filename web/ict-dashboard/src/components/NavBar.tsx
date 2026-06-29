// ---------------------------------------------------------------------------------------------------
// NavBar — the top navigation for the 6-page app (Live · Signals · Trades · Backtest · Optimizer ·
// Settings, plan §15). Lives in the shared header. The "Advisory · Paper only" guardrail badge + the NY
// clock stay visible on every page (the defensive posture surfaced at the UI, §6.3). Read-only: no
// order/execute control.
// ---------------------------------------------------------------------------------------------------

import { NavLink } from 'react-router-dom';
import { useNyClock } from '../hooks/useNyClock';
import { NotificationCenter } from '../notifications/NotificationCenter';
import { SystemHealthIndicator } from '../notifications/SystemHealthIndicator';

const LINKS: readonly { to: string; label: string }[] = [
  { to: '/', label: 'Live' },
  { to: '/signals', label: 'Signals' },
  { to: '/trades', label: 'Trades' },
  { to: '/backtest', label: 'Backtest' },
  { to: '/optimizer', label: 'Optimizer' },
  { to: '/settings', label: 'Settings' },
];

export function NavBar(): React.JSX.Element {
  const clock = useNyClock();
  return (
    <header className="app__header">
      <div className="app__brand">
        ICT Trading Desk
        <small>analysis &amp; paper-trading · read-only</small>
      </div>

      <nav className="nav" aria-label="Primary">
        {LINKS.map((l) => (
          <NavLink
            key={l.to}
            to={l.to}
            end={l.to === '/'}
            className={({ isActive }) => `nav__link${isActive ? ' nav__link--active' : ''}`}
          >
            {l.label}
          </NavLink>
        ))}
      </nav>

      <div className="app__tools">
        <SystemHealthIndicator />
        <NotificationCenter />
        <span className="badge-advisory">Advisory · Paper only</span>
        <span className="app__clock">{clock}</span>
      </div>
    </header>
  );
}
