// ---------------------------------------------------------------------------------------------------
// Notification store — a dependency-free, module-level pub/sub backing the toast viewport + the
// notification center (the operator's TOP complaint: "notifications cannot be closed"). There is NO
// toast/snackbar npm package — this is a tiny external store consumed via `useSyncExternalStore`
// (src/notifications/useNotifications.ts), so React subscribes to the same singleton the trigger sites
// (useDashboardData / a failed mutation) push into.
//
// Two surfaces read the same notices:
//   - ToastViewport renders the ACTIVE (not-yet-dismissed) toasts top-right, each closable.
//   - NotificationCenter (a NavBar bell + popover) renders the full HISTORY (newest-first) so an
//     auto-dismissed success/info toast is never "lost" — it stays in the durable record until cleared.
//
// DEFENSIVE: an `error` notice is STICKY (autoDismissMs: null) so a real backend failure can never
// silently auto-vanish; success/info default to ~6s. History is capped (~50) so it can't grow forever.
// ---------------------------------------------------------------------------------------------------

/** The kinds of notice the dashboard raises. Drives the toast's left-accent colour + the center icon. */
export type NoticeKind = 'opportunity' | 'tradeOpened' | 'tradeClosed' | 'error' | 'info';

/** A focus payload — when present the toast/center row offers a "View on chart" action (symbol + instant). */
export interface NoticeFocus {
  symbol: string;
  atUtc?: string;
}

/** One notification. `atUtc` is an ISO-8601 UTC instant (rendered in NY); `id` is unique + stable. */
export interface Notice {
  id: string;
  kind: NoticeKind;
  title: string;
  body?: string;
  symbol?: string;
  atUtc: string;
  /**
   * Auto-dismiss delay in ms, or `null` to stay until explicitly dismissed. ERRORS are always sticky
   * (null) — see {@link DEFAULT_AUTO_DISMISS_MS}. `undefined` falls back to the per-kind default.
   */
  autoDismissMs?: number | null;
  /** Optional chart-focus target — surfaces a "View on chart" action. */
  focus?: NoticeFocus;
  /** Read-state for the NavBar unread badge (toasts mark a notice read once dismissed/seen). */
  read: boolean;
}

/** The input to {@link notify} — everything except the generated `id`/`atUtc`/`read` bookkeeping. */
export interface NotifyInput {
  kind: NoticeKind;
  title: string;
  body?: string;
  symbol?: string;
  /** Override the auto-dismiss; ignored for `error` (always sticky). Defaults per-kind. */
  autoDismissMs?: number | null;
  focus?: NoticeFocus;
  /** Optional explicit instant (ISO UTC); defaults to now. */
  atUtc?: string;
  /** Optional explicit id (a trigger may use a deterministic id to de-dupe); defaults to a generated one. */
  id?: string;
}

/** Max notices kept in history; older ones drop off the end (newest are at the front). */
export const HISTORY_CAP = 50;

/** Per-kind default auto-dismiss. ERRORS are STICKY (null) — a backend failure must not auto-vanish. */
const DEFAULT_AUTO_DISMISS_MS: Record<NoticeKind, number | null> = {
  opportunity: 8000,
  tradeOpened: 6000,
  tradeClosed: 6000,
  error: null, // sticky — the defensive signal
  info: 6000,
};

let notices: Notice[] = [];
const listeners = new Set<() => void>();
let seq = 0;

function emit(): void {
  for (const l of listeners) l();
}

function nextId(): string {
  seq += 1;
  // Monotonic + time-seeded so ids stay unique across a session even if two fire in the same ms.
  return `n_${Date.now().toString(36)}_${seq.toString(36)}`;
}

/**
 * Raise a notice. Returns its id (so a caller can dismiss it later). `error` notices are ALWAYS sticky
 * regardless of the passed `autoDismissMs`. History is capped at {@link HISTORY_CAP}.
 */
export function notify(input: NotifyInput): string {
  const id = input.id ?? nextId();
  // An explicit id that already exists is a no-op de-dupe (a trigger may re-fire on a poll reconcile).
  if (input.id && notices.some((n) => n.id === input.id)) {
    return input.id;
  }
  const autoDismissMs =
    input.kind === 'error'
      ? null
      : input.autoDismissMs !== undefined
        ? input.autoDismissMs
        : DEFAULT_AUTO_DISMISS_MS[input.kind];

  const notice: Notice = {
    id,
    kind: input.kind,
    title: input.title,
    body: input.body,
    symbol: input.symbol,
    atUtc: input.atUtc ?? new Date().toISOString(),
    autoDismissMs,
    focus: input.focus,
    read: false,
  };

  notices = [notice, ...notices].slice(0, HISTORY_CAP);
  emit();
  return id;
}

/** Mark a notice read (it leaves the active toast stack but stays in history). No-op if unknown. */
export function dismiss(id: string): void {
  let changed = false;
  notices = notices.map((n) => {
    if (n.id === id && !n.read) {
      changed = true;
      return { ...n, read: true };
    }
    return n;
  });
  if (changed) emit();
}

/** Mark every notice read (clears the unread badge + the active toast stack) without losing history. */
export function markAllRead(): void {
  if (notices.every((n) => n.read)) return;
  notices = notices.map((n) => (n.read ? n : { ...n, read: true }));
  emit();
}

/** Drop the entire history (the "Clear all" action). */
export function clearAll(): void {
  if (notices.length === 0) return;
  notices = [];
  emit();
}

/** Subscribe to store changes (for useSyncExternalStore). Returns an unsubscribe. */
export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** The current notices snapshot (newest-first). STABLE reference between emits (useSyncExternalStore contract). */
export function getSnapshot(): readonly Notice[] {
  return notices;
}

/** The active toasts = unread notices (the dismissed/seen ones drop out of the stack but stay in history). */
export function activeToasts(): readonly Notice[] {
  return notices.filter((n) => !n.read);
}

/** Count of unread notices — drives the NavBar bell badge. */
export function unreadCount(): number {
  return notices.reduce((c, n) => c + (n.read ? 0 : 1), 0);
}

/** TEST-ONLY: reset the module store between tests (the singleton survives across renders otherwise). */
export function __resetNotificationsForTest(): void {
  notices = [];
  seq = 0;
  emit();
}
