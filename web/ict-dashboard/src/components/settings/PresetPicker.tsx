// ---------------------------------------------------------------------------------------------------
// PresetPicker — three one-click presets (Strict §2.5 / Balanced / Discovery) that FILL the per-instrument
// override form so a non-expert can configure the model without understanding every knob. Applying a
// preset does NOT auto-save — it loads the values into the form, shows a one-line "what this changes vs
// default" diff (and a caution for the looser ones), and the operator reviews then presses Save.
//
// A preset maps onto the EXISTING InstrumentSettingsDto fields only (k-of-n / required subset) — no new
// wire fields. The DisplacementMss direction lock is preserved automatically (the relaxing presets leave
// the subset empty = inherit the global required set). Read-only/advisory; no order control (§6.3).
// ---------------------------------------------------------------------------------------------------

import { useState } from 'react';
import type { InstrumentSettingsDto } from '../../types/api';
import { PRESETS, presetDiffSummary, type PresetId, type SettingsPreset } from '../../settings/presets';
import { InfoTip } from '../InfoTip';

interface PresetPickerProps {
  /** The number of available required conditions (n) — the presets relax k against this. */
  availableCount: number;
  /** Apply the preset's built fields into the form (the parent merges onto the current override). */
  onApply: (
    fields: Pick<InstrumentSettingsDto, 'minRequiredConditions' | 'requiredConditions'>,
  ) => void;
}

export function PresetPicker({ availableCount, onApply }: PresetPickerProps): React.JSX.Element {
  const [applied, setApplied] = useState<PresetId | null>(null);
  const [diff, setDiff] = useState('');
  const [warning, setWarning] = useState('');

  function apply(preset: SettingsPreset): void {
    const fields = preset.build(availableCount);
    onApply(fields);
    setApplied(preset.id);
    setDiff(presetDiffSummary(fields, availableCount));
    setWarning(preset.warning ?? '');
  }

  return (
    <div className="presets" aria-label="Quick presets">
      <div className="presets__head">
        <span className="presets__title">
          Quick presets
          <InfoTip
            title="Presets"
            label="Help: presets"
          >
            One click fills the form with a ready-made configuration. Review the change, then press Save —
            nothing is saved until you do.
          </InfoTip>
        </span>
      </div>

      <div className="presets__grid">
        {PRESETS.map((p) => (
          <button
            key={p.id}
            type="button"
            className={`preset-card${applied === p.id ? ' preset-card--applied' : ''}`}
            aria-pressed={applied === p.id}
            onClick={() => apply(p)}
          >
            <span className="preset-card__label">{p.label}</span>
            <span className="preset-card__blurb">{p.blurb}</span>
          </button>
        ))}
      </div>

      {diff ? (
        <p className="presets__diff" role="status">
          {diff}
        </p>
      ) : null}
      {warning ? (
        <p className="presets__warn" role="note">
          {warning}
        </p>
      ) : null}
    </div>
  );
}
