// ---------------------------------------------------------------------------------------------------
// Pure signal helpers shared by the SignalsFeed + TopSignalsPanel (kept out of the component files so
// they stay Fast-Refresh-clean — a component module must export only components). Read-only/advisory:
// "takeable" decides whether a Take (paper) button is offered, never anything that places a live order.
// ---------------------------------------------------------------------------------------------------

import type { RankedSignalDto } from '../types/api';

/**
 * Whether a signal is takeable now: only Manual entry mode, not already taken, and no block reason. An
 * Auto signal opens itself (the engine arms/opens it), so it is never "takeable" by the operator.
 */
export function isTakeable(signal: RankedSignalDto): boolean {
  return signal.entryMode !== 'Auto' && !signal.isTaken && !signal.blockReason;
}

/** A human-readable disabled reason for a non-takeable Manual signal (drives the Take button tooltip). */
export function blockedReason(signal: RankedSignalDto): string | null {
  if (signal.blockReason === 'Expired') return 'Signal expired';
  if (signal.isTaken || signal.blockReason === 'AlreadyTaken') return 'Already taken';
  if (signal.blockReason) return signal.blockReason;
  return null;
}
