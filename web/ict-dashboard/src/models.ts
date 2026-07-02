// ---------------------------------------------------------------------------------------------------
// ICT setup-model constants + label helpers (plan §9). The backend added a "setup model" dimension —
// the SetupModel enum with wire member names "Ict2022" / "Ict2024". This is the UI's label map + the
// fallback option set: pages prefer the LIVE `availableModels` from GET /api/settings where natural, and
// fall back to MODELS here. Kept a tiny standalone module (like theme.ts / format.ts) so both components
// and non-component modules can import it without tripping Fast-Refresh's only-export-components rule.
// ---------------------------------------------------------------------------------------------------

/** The frozen SetupModel wire enum member names. */
export type SetupModel = 'Ict2022' | 'Ict2024';

/** The server default when a model is absent (old payloads / unset). */
export const DEFAULT_MODEL: SetupModel = 'Ict2022';

/** The selectable models — value (wire name) + display label. The fallback option set for multi-selects. */
export const MODELS: readonly { value: SetupModel; label: string }[] = [
  { value: 'Ict2022', label: 'ICT 2022' },
  { value: 'Ict2024', label: 'ICT 2024' },
];

const MODEL_LABELS: Readonly<Record<string, string>> = {
  Ict2022: 'ICT 2022',
  Ict2024: 'ICT 2024',
};

/** The display label for a model wire name ("ICT 2022"); unknown → the raw name; absent → the default. */
export function modelLabel(model: string | null | undefined): string {
  const m = model || DEFAULT_MODEL;
  return MODEL_LABELS[m] ?? m;
}

/** A short chip label ("2022" / "2024") for a model wire name; unknown → the raw name; absent → default. */
export function modelBadgeText(model: string | null | undefined): string {
  const m = model || DEFAULT_MODEL;
  if (m === 'Ict2022') return '2022';
  if (m === 'Ict2024') return '2024';
  return m;
}
