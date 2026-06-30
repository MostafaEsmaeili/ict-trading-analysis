// ---------------------------------------------------------------------------------------------------
// InstrumentOverrideForm — the editable, LIVE per-instrument override for ONE symbol (extracted from the
// monolithic SettingsPage). It is keyed by the symbol (+ a reload nonce) in the parent, so React remounts
// it on a symbol switch or after a save/clear — state initialises from `initial` via lazy useState (the
// idiomatic uncontrolled-with-a-key reset, NO effect).
//
// Redesigned for a non-expert: presets fill the form in one click; every jargon term has an InfoTip; the
// number fields (OverrideField) show the inherited global default as ghost text + a "vs default" chip when
// overridden. The wire contract (InstrumentSettingsDto, PUT /api/settings/instruments/{symbol}) is
// UNCHANGED. A required subset must include DisplacementMss (mirrors the backend; the server validates too).
// Read-only/advisory: saving only tunes what the scanner CONSIDERS — there is no order/execute path (§6.3).
// ---------------------------------------------------------------------------------------------------

import { useState } from 'react';
import type { UseMutationResult } from '@tanstack/react-query';
import type { GlobalConceptSettingsDto, InstrumentSettingsDto } from '../../types/api';
import { errorMessage } from '../../format-error';
import { InfoTip } from '../InfoTip';
import { OverrideField } from './OverrideField';
import { PresetPicker } from './PresetPicker';
import { DIRECTION_LOCK } from '../../settings/presets';

type UpdateMutation = UseMutationResult<
  void,
  Error,
  { symbol: string; body: InstrumentSettingsDto | null }
>;

/** Parse a form string into a wire number-or-null ("" → null = inherit). */
function toNumberOrNull(raw: string): number | null {
  const t = raw.trim();
  if (t === '') return null;
  const n = Number(t);
  return Number.isFinite(n) ? n : null;
}

export function InstrumentOverrideForm({
  symbol,
  initial,
  available,
  global,
  hasOverride,
  update,
  onMutated,
}: {
  symbol: string;
  initial: InstrumentSettingsDto | undefined;
  available: string[];
  /** The global defaults — surfaced as inherited ghost text beside each field. */
  global: GlobalConceptSettingsDto | undefined;
  hasOverride: boolean;
  update: UpdateMutation;
  onMutated: () => void;
}): React.JSX.Element {
  const [minK, setMinK] = useState(() =>
    initial?.minRequiredConditions != null ? String(initial.minRequiredConditions) : '',
  );
  const [subset, setSubset] = useState<Set<string>>(() => new Set(initial?.requiredConditions ?? []));
  const [minStop, setMinStop] = useState(() =>
    initial?.minStopDistancePips != null ? String(initial.minStopDistancePips) : '',
  );
  const [spread, setSpread] = useState(() =>
    initial?.spreadBasePips != null ? String(initial.spreadBasePips) : '',
  );
  const [commission, setCommission] = useState(() =>
    initial?.commissionPerLotRoundTripUsd != null ? String(initial.commissionPerLotRoundTripUsd) : '',
  );
  // Tri-state (the DTO field is nullable): inherit the global default (null), require for this symbol (true),
  // or explicitly disable it (false) — collapsing false→null would silently drop a per-symbol opt-out.
  const [htfBias, setHtfBias] = useState<'inherit' | 'required' | 'disabled'>(() =>
    initial?.requireReferenceOpenAgreement == null
      ? 'inherit'
      : initial.requireReferenceOpenAgreement
        ? 'required'
        : 'disabled',
  );
  // Entry mode is a 3-state nullable field: inherit (null) / Auto / Manual. Auto = the engine arms/opens
  // the paper trade itself; Manual = it is only ranked/alerted and the operator takes it (Signals page).
  const [entryMode, setEntryMode] = useState<'inherit' | 'Auto' | 'Manual'>(() =>
    initial?.entryMode === 'Auto' || initial?.entryMode === 'Manual' ? initial.entryMode : 'inherit',
  );
  const [localError, setLocalError] = useState('');

  function toggleCondition(c: string): void {
    setSubset((prev) => {
      const next = new Set(prev);
      if (next.has(c)) next.delete(c);
      else next.add(c);
      return next;
    });
  }

  // A preset fills the k-of-n + required-subset fields (the operator reviews, then Saves).
  function applyPreset(
    fields: Pick<InstrumentSettingsDto, 'minRequiredConditions' | 'requiredConditions'>,
  ): void {
    setLocalError('');
    setMinK(fields.minRequiredConditions != null ? String(fields.minRequiredConditions) : '');
    setSubset(new Set(fields.requiredConditions ?? []));
  }

  function onSave(e: React.FormEvent): void {
    e.preventDefault();
    setLocalError('');
    // Mirror the backend guard so the operator gets instant feedback (the server validates too).
    if (subset.size > 0 && !subset.has(DIRECTION_LOCK)) {
      setLocalError(`A required subset must include ${DIRECTION_LOCK} (the FSM direction lock).`);
      return;
    }
    const body: InstrumentSettingsDto = {
      minRequiredConditions: toNumberOrNull(minK),
      requiredConditions: subset.size > 0 ? [...subset] : null,
      minStopDistancePips: toNumberOrNull(minStop),
      spreadBasePips: toNumberOrNull(spread),
      commissionPerLotRoundTripUsd: toNumberOrNull(commission),
      // inherit → null (use the global default); required → true; disabled → false (explicit opt-out).
      requireReferenceOpenAgreement: htfBias === 'inherit' ? null : htfBias === 'required',
      // inherit → null (global default); else the explicit per-symbol Auto/Manual entry mode.
      entryMode: entryMode === 'inherit' ? null : entryMode,
    };
    update.mutate({ symbol, body }, { onSuccess: onMutated });
  }

  function onClear(): void {
    setLocalError('');
    update.mutate({ symbol, body: null }, { onSuccess: onMutated });
  }

  const errorText = localError || (update.isError ? errorMessage(update.error) : '');

  return (
    <div className="ovform">
      <PresetPicker availableCount={available.length} onApply={applyPreset} />

      <form className="form form--grid ovform__form" onSubmit={onSave} aria-label="Instrument override form">
        <OverrideField
          label="Min required (k of n)"
          term="kOfN"
          value={minK}
          onChange={setMinK}
          globalDefault={global?.minRequiredConditions ?? null}
        />

        <fieldset className="multi multi--subset" aria-label="Required-condition subset">
          <legend>
            Required subset
            <InfoTip term="requiredSubset" />
            <span className="multi__hint"> — none = inherit global</span>
          </legend>
          <div className="multi__opts">
            {available.map((c) => {
              const on = subset.has(c);
              const lock = c === DIRECTION_LOCK;
              return (
                <button
                  key={c}
                  type="button"
                  className={`chip-toggle${on ? ' chip-toggle--on' : ''}${lock ? ' chip-toggle--lock' : ''}`}
                  aria-pressed={on}
                  onClick={() => toggleCondition(c)}
                  title={lock ? 'Required in any subset (FSM direction lock)' : undefined}
                >
                  {c}
                  {lock ? ' *' : ''}
                </button>
              );
            })}
          </div>
          <p className="multi__note">
            * <strong>{DIRECTION_LOCK}</strong> is the direction lock — it must be in any subset you pick.
          </p>
        </fieldset>

        <OverrideField
          label="Min stop (pips)"
          term="minStop"
          value={minStop}
          onChange={setMinStop}
          globalDefault={global?.minStopDistancePips ?? null}
          unit="pips"
        />
        <OverrideField
          label="Spread base (pips)"
          term="spreadCommission"
          value={spread}
          onChange={setSpread}
          globalDefault={global?.spreadBasePips ?? null}
          unit="pips"
        />
        <OverrideField
          label="Commission ($/lot round-trip)"
          term="spreadCommission"
          value={commission}
          onChange={setCommission}
          globalDefault={global?.commissionPerLotRoundTripUsd ?? null}
          unit="$"
        />

        <label className="form__field">
          <span>
            HTF daily-bias agreement
            <InfoTip term="htfBias" />
          </span>
          <select
            className="input"
            aria-label="HTF daily-bias agreement"
            value={htfBias}
            onChange={(e) => setHtfBias(e.target.value as 'inherit' | 'required' | 'disabled')}
          >
            <option value="inherit">Inherit global default</option>
            <option value="required">Require for this symbol</option>
            <option value="disabled">Disable for this symbol</option>
          </select>
        </label>

        <label className="form__field">
          <span>
            Entry mode
            <InfoTip term="entryMode" />
          </span>
          <select
            className="input"
            aria-label="Entry mode"
            value={entryMode}
            onChange={(e) => setEntryMode(e.target.value as 'inherit' | 'Auto' | 'Manual')}
          >
            <option value="inherit">Inherit global default</option>
            <option value="Auto">Auto — engine opens it</option>
            <option value="Manual">Manual — you take it</option>
          </select>
        </label>

        <div className="form__actions">
          <button type="submit" className="btn btn--primary" disabled={update.isPending || !symbol}>
            {update.isPending ? 'Saving…' : 'Save override'}
          </button>
          <button
            type="button"
            className="btn"
            onClick={onClear}
            disabled={update.isPending || !hasOverride}
            title={hasOverride ? 'Revert this symbol to the catalog default' : 'No override to clear'}
          >
            Clear override
          </button>
        </div>

        {errorText ? (
          <p className="empty error" role="alert">
            {errorText}
          </p>
        ) : null}
      </form>
    </div>
  );
}
