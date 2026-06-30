// ---------------------------------------------------------------------------------------------------
// NotificationCenter — the NavBar bell + unread badge that opens a popover of the FULL notice history
// (newest-first). This is the durable record: an auto-dismissed success/info toast isn't "lost" — it
// lives here until "Clear all". Each row is dismissible; "Mark all read" clears the unread badge (and
// the active toast stack) without discarding history. Read-only — no order/execute control (§6.3).
// ---------------------------------------------------------------------------------------------------

import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { formatNyDateTime } from '../time';
import { clearAll, dismiss, markAllRead, type Notice } from './notificationStore';
import { useNotifications, useUnreadCount } from './useNotifications';

const KIND_LABEL: Record<Notice['kind'], string> = {
  opportunity: 'Opportunity',
  tradeOpened: 'Trade opened',
  tradeClosed: 'Trade closed',
  error: 'Error',
  info: 'Notice',
};

export function NotificationCenter(): React.JSX.Element {
  const notices = useNotifications();
  const unread = useUnreadCount();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  // Close the popover on an outside click or Escape (a popover the operator can't dismiss is the same
  // complaint we're fixing — keep every transient surface closable).
  useEffect(() => {
    if (!open) return;
    const onDocClick = (e: MouseEvent): void => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const onView = (notice: Notice): void => {
    if (!notice.focus) return;
    const params = new URLSearchParams({ symbol: notice.focus.symbol });
    navigate(`/?${params.toString()}`);
    dismiss(notice.id);
    setOpen(false);
  };

  return (
    <div className="notice-center" ref={rootRef}>
      <button
        type="button"
        className="notice-bell"
        aria-label={`Notifications${unread > 0 ? ` (${unread} unread)` : ''}`}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
      >
        <span aria-hidden="true">🔔</span>
        {unread > 0 ? (
          <span className="notice-bell__badge num" aria-hidden="true">
            {unread > 99 ? '99+' : unread}
          </span>
        ) : null}
      </button>

      {open ? (
        <div className="notice-pop" role="dialog" aria-label="Notification history">
          <header className="notice-pop__head">
            <span>Notifications</span>
            <div className="notice-pop__actions">
              <button
                type="button"
                className="notice-link"
                onClick={markAllRead}
                disabled={unread === 0}
              >
                Mark all read
              </button>
              <button
                type="button"
                className="notice-link"
                onClick={clearAll}
                disabled={notices.length === 0}
              >
                Clear all
              </button>
            </div>
          </header>

          <div className="notice-pop__list">
            {notices.length === 0 ? (
              <p className="empty">No notifications.</p>
            ) : (
              notices.map((n) => (
                <div
                  key={n.id}
                  className={`notice-row notice-row--${n.kind}${n.read ? '' : ' notice-row--unread'}`}
                >
                  <div className="notice-row__body">
                    <div className="notice-row__head">
                      <span className="notice-row__kind">{KIND_LABEL[n.kind]}</span>
                      <span className="notice-row__time num">{formatNyDateTime(n.atUtc)} NY</span>
                    </div>
                    <p className="notice-row__title">{n.title}</p>
                    {n.body ? <p className="notice-row__text">{n.body}</p> : null}
                    {n.focus ? (
                      <button type="button" className="notice-link" onClick={() => onView(n)}>
                        View on chart
                      </button>
                    ) : null}
                  </div>
                  <button
                    type="button"
                    className="notice-row__close"
                    aria-label="Dismiss notification"
                    onClick={() => dismiss(n.id)}
                  >
                    ✕
                  </button>
                </div>
              ))
            )}
          </div>
        </div>
      ) : null}
    </div>
  );
}
