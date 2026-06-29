// ---------------------------------------------------------------------------------------------------
// Settings presets — pure client constants (no wire contract change). A preset is a ready-made
// per-instrument override an operator can apply with one click, then review and Save. It maps onto the
// EXISTING InstrumentSettingsDto fields only (k-of-n + required subset) — it never invents new fields.
//
// Three presets, from safest to loosest:
//   - Strict §2.5   — the canonical all-required model (no relaxation). The global default.
//   - Balanced      — k = 6 of 8 (a couple of checks may be missing). More signals, slightly lower grade.
//   - Discovery     — k = 4 of 8 (loose). Many more signals, clearly lower average grade (warned).
//
// Applying a preset FILLS the form (it does not auto-save); the operator reviews the one-line diff and
// presses Save. The DisplacementMss direction lock is mandatory in any required subset (mirrors the
// backend guard) — presets that relax k-of-n leave the subset empty (inherit the global required set),
// so the lock is preserved automatically.
// ---------------------------------------------------------------------------------------------------

import type { InstrumentSettingsDto } from '../types/api';

/** The FSM direction lock — must be present in any explicit required subset (mirrors the backend). */
export const DIRECTION_LOCK = 'DisplacementMss';

/** A preset's stable id (used as the React key + the chosen-preset marker). */
export type PresetId = 'strict' | 'balanced' | 'discovery';

export interface SettingsPreset {
  id: PresetId;
  label: string;
  /** A one-line summary under the preset button. */
  blurb: string;
  /** A non-blocking caution shown when the preset trades quality for quantity (empty = none). */
  warning?: string;
  /**
   * The override this preset applies, given the canonical required set `n` (the available conditions).
   * Returns ONLY the existing InstrumentSettingsDto fields — cost/min-stop fields are left untouched
   * (the caller merges this onto the current form so a preset never wipes a hand-set cost).
   */
  build: (availableCount: number) => Pick<
    InstrumentSettingsDto,
    'minRequiredConditions' | 'requiredConditions'
  >;
}

export const PRESETS: readonly SettingsPreset[] = [
  {
    id: 'strict',
    label: 'Strict §2.5',
    blurb: 'The canonical ICT model — every required check must pass. Fewest signals, highest quality.',
    build: () => ({ minRequiredConditions: null, requiredConditions: null }),
  },
  {
    id: 'balanced',
    label: 'Balanced',
    blurb: 'Allow a couple of checks to be missing (k = 6 of 8). More signals, slightly lower average grade.',
    build: (n) => ({ minRequiredConditions: Math.min(6, n), requiredConditions: null }),
  },
  {
    id: 'discovery',
    label: 'Discovery',
    blurb: 'Loosest gate (k = 4 of 8). Many more signals to watch.',
    warning: 'More signals, but a lower average grade — treat these as ideas to review, not high-confidence trades.',
    build: (n) => ({ minRequiredConditions: Math.min(4, n), requiredConditions: null }),
  },
] as const;

/** Look up a preset by id (undefined if none). */
export function presetById(id: PresetId): SettingsPreset | undefined {
  return PRESETS.find((p) => p.id === id);
}

/**
 * A human one-line "what this changes vs the strict default" for a built override. Pure string — used by
 * the PresetPicker to show the operator what they’re about to apply before they Save.
 */
export function presetDiffSummary(
  built: Pick<InstrumentSettingsDto, 'minRequiredConditions' | 'requiredConditions'>,
  availableCount: number,
): string {
  const k = built.minRequiredConditions;
  if (k == null) {
    return `Strict: requires all ${availableCount} conditions (the default).`;
  }
  return `Relaxed: requires ${k} of ${availableCount} conditions (vs all ${availableCount} by default).`;
}
