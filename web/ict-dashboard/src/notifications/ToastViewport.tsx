// ---------------------------------------------------------------------------------------------------
// ToastViewport — the top-right stack of ACTIVE toasts (the operator's TOP complaint: there was no way
// to close a notification — only sticky red error text + an ever-growing alerts feed). Each toast has:
//   - a kind-coloured left accent (green tradeOpened/tradeClosed-win, red error/loss, amber opportunity)
//   - title / optional body / a NY timestamp
//   - an EXPLICIT close button (aria-label "Dismiss notification") — the missing affordance
//   - pause-on-hover auto-dismiss (respecting prefers-reduced-motion: a reduced-motion user gets no
//     auto-dismiss timer so a notice never vanishes from under them)
//   - a "View on chart" action when the notice carries a focus payload.
//
// Mounted ONCE in AppLayout so it overlays every route. ERROR toasts are sticky (autoDismissMs null) —
// a real backend failure must not auto-vanish (the defensive signal, §6.3).
// ---------------------------------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { formatNyDateTime } from '../time';
import { dismiss, type Notice } from './notificationStore';
import { useActiveToasts } from './useNotifications';

/** Human label for each kind (shown small above the title). */
const KIND_LABEL: Record<Notice['kind'], string> = {
  opportunity: 'Opportunity',
  tradeOpened: 'Trade opened',
  tradeClosed: 'Trade closed',
  error: 'Error',
  info: 'Notice',
};

function prefersReducedMotion(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  );
}

/** A single toast card — owns its own pause-on-hover auto-dismiss timer. */
function Toast({ notice }: { notice: Notice }): React.JSX.Element {
  const navigate = useNavigate();
  const [paused, setPaused] = useState(false);
  // A reduced-motion user gets NO auto-dismiss (the notice waits for an explicit close). Read once via
  // a lazy state initializer (a render-time media check is stable for the toast's lifetime).
  const [reduced] = useState(prefersReducedMotion);

  useEffect(() => {
    if (paused || reduced) return;
    if (notice.autoDismissMs == null) return; // sticky (errors)
    const t = window.setTimeout(() => dismiss(notice.id), notice.autoDismissMs);
    return () => window.clearTimeout(t);
    // Re-arm whenever pause toggles so hover pauses + resumes the countdown.
  }, [notice.id, notice.autoDismissMs, paused, reduced]);

  const onView = (): void => {
    if (!notice.focus) return;
    const params = new URLSearchParams({ symbol: notice.focus.symbol });
    navigate(`/?${params.toString()}`);
    dismiss(notice.id);
  };

  return (
    <div
      className={`toast toast--${notice.kind}`}
      role={notice.kind === 'error' ? 'alert' : 'status'}
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onFocus={() => setPaused(true)}
      onBlur={() => setPaused(false)}
    >
      <div className="toast__body">
        <div className="toast__head">
          <span className="toast__kind">{KIND_LABEL[notice.kind]}</span>
          <span className="toast__time num">{formatNyDateTime(notice.atUtc)} NY</span>
        </div>
        <p className="toast__title">{notice.title}</p>
        {notice.body ? <p className="toast__text">{notice.body}</p> : null}
        {notice.focus ? (
          <button type="button" className="toast__action" onClick={onView}>
            View on chart
          </button>
        ) : null}
      </div>
      <button
        type="button"
        className="toast__close"
        aria-label="Dismiss notification"
        onClick={() => dismiss(notice.id)}
      >
        ✕
      </button>
    </div>
  );
}

export function ToastViewport(): React.JSX.Element {
  const toasts = useActiveToasts();
  return (
    <div className="toast-viewport" aria-live="polite" aria-label="Notifications">
      {toasts.map((n) => (
        <Toast key={n.id} notice={n} />
      ))}
    </div>
  );
}
