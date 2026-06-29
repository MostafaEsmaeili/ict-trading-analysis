// ---------------------------------------------------------------------------------------------------
// SystemHealthIndicator — the NavBar green/amber/red dot. Bound to useSystemHealth (core query error
// state + SignalR connection), NOT to the toast store: dismissing a toast can never turn this green
// while the backend is still failing (the operator's explicit requirement, §6.3). Its tooltip lists the
// failing endpoints so a red dot says WHAT is down.
// ---------------------------------------------------------------------------------------------------

import { useSystemHealth, type HubHealth } from './useSystemHealth';

const LABEL: Record<ReturnType<typeof useSystemHealth>['status'], string> = {
  green: 'All systems healthy',
  amber: 'Live updates degraded',
  red: 'Backend error',
};

const HUB_TEXT: Record<HubHealth, string> = {
  connected: 'live push connected',
  connecting: 'live push reconnecting',
  disconnected: 'live push disconnected',
  disabled: 'polling (no live push)',
};

export interface SystemHealthIndicatorProps {
  /** The SignalR hub health (from useDashboardData). Omit in mocks/tests → treated as 'disabled'. */
  hub?: HubHealth;
}

export function SystemHealthIndicator({ hub }: SystemHealthIndicatorProps): React.JSX.Element {
  const health = useSystemHealth({ hub });

  const title =
    health.status === 'red'
      ? `${LABEL.red} — ${health.failing.join(', ')} unavailable`
      : `${LABEL[health.status]} · ${HUB_TEXT[health.hub]}`;

  return (
    <span
      className={`health-dot health-dot--${health.status}`}
      role="status"
      aria-label={title}
      title={title}
    >
      <span className="health-dot__pip" aria-hidden="true" />
      <span className="health-dot__text">
        {health.status === 'red' ? 'Issue' : health.status === 'amber' ? 'Degraded' : 'Live'}
      </span>
    </span>
  );
}
