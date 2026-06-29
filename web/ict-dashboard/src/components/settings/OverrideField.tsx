// ---------------------------------------------------------------------------------------------------
// OverrideField — a labelled number input for a per-instrument override that makes the INHERITANCE
// explicit, so a non-expert understands an empty field is not "broken" but "use the global default".
//
//   - empty  → placeholder reads "inherit (global: <default>)" so the effective value is visible.
//   - filled → a small "vs default" delta chip appears beside the label (e.g. "vs 10 →"), so the
//              operator sees they have changed it and by how much.
//
// The global default comes from the settings `global` block already on the wire — this only SURFACES it,
// it does not change the contract. Read-only/advisory; tuning never places an order (§6.3).
// ---------------------------------------------------------------------------------------------------

import { InfoTip } from '../InfoTip';
import type { GlossaryTerm } from '../../settings/glossary';

interface OverrideFieldProps {
  label: string;
  /** Current form value (string; "" = inherit). */
  value: string;
  onChange: (v: string) => void;
  /** The inherited global default to show as ghost text + the delta baseline (undefined = unknown). */
  globalDefault?: number | null;
  /** Optional unit suffix for the ghost/delta text (e.g. "pips", "$"). */
  unit?: string;
  /** Optional glossary term → an InfoTip beside the label. */
  term?: GlossaryTerm;
}

/** Format the inherited default for the placeholder/delta (em-dash when unknown). */
function fmtDefault(value: number | null | undefined, unit?: string): string {
  if (value == null) return '—';
  return unit ? `${value} ${unit}` : String(value);
}

export function OverrideField({
  label,
  value,
  onChange,
  globalDefault,
  unit,
  term,
}: OverrideFieldProps): React.JSX.Element {
  const hasValue = value.trim() !== '';
  const defaultText = fmtDefault(globalDefault, unit);

  return (
    <label className="form__field ovf">
      <span className="ovf__label">
        {label}
        {term ? <InfoTip term={term} /> : null}
        {hasValue ? (
          <span className="ovf__delta" title={`Overridden — the global default is ${defaultText}`}>
            vs {defaultText}
          </span>
        ) : null}
      </span>
      <input
        type="number"
        className="input"
        aria-label={label}
        step="any"
        placeholder={globalDefault == null ? 'inherit' : `inherit (global: ${defaultText})`}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </label>
  );
}
