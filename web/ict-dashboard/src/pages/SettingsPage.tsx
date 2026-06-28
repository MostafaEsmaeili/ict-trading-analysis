// ---------------------------------------------------------------------------------------------------
// SettingsPage — the operator's live settings (plan §15, Workstream D). Two sections:
//
//   1. Per-instrument overrides — EDITABLE, LIVE (no restart). Pick a symbol, set/clear its k-of-n
//      (MinRequiredConditions), its required-condition subset, and its per-pair cost geometry. A save
//      PUTs /api/settings/instruments/{symbol}; the runtime store bumps its revision so the scanner /
//      orchestrator caches rebuild on the next candle — the next backtest/scan already reflects it.
//   2. Global concept settings — comprehensive READ-ONLY view (confluence/grading, risk ladder,
//      execution costs, active killzones/styles). These are bound from Ict:* at startup; the editable
//      surface is the per-instrument override above.
//
// Read-only/advisory: changing settings only tunes what the scanner CONSIDERS — there is no order or
// execute path anywhere (§6.3 guardrail).
// ---------------------------------------------------------------------------------------------------

import { useMemo, useState } from 'react';
import type { UseMutationResult } from '@tanstack/react-query';
import { useCalendar, useConfig, useSettings, useUpdateInstrumentSettings } from '../api/hooks';
import type { CalendarStatusDto, GlobalConceptSettingsDto, InstrumentSettingsDto } from '../types/api';
import { formatPct, formatPercentValue } from '../format';
import { errorMessage } from '../format-error';

// The FSM direction lock — a required subset MUST include it (the backend rejects a subset without it).
const DIRECTION_LOCK = 'DisplacementMss';

type UpdateMutation = UseMutationResult<
  void,
  Error,
  { symbol: string; body: InstrumentSettingsDto | null }
>;

function CfgRow({ label, children }: { label: string; children: React.ReactNode }): React.JSX.Element {
  return (
    <div className="cfg__row">
      <span className="cfg__label">{label}</span>
      <span className="cfg__value">{children}</span>
    </div>
  );
}

/** A number field whose empty value means "inherit the catalog default" (null on the wire). */
function NumberField({
  label,
  value,
  onChange,
  placeholder = 'inherit',
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
}): React.JSX.Element {
  return (
    <label className="form__field">
      <span>{label}</span>
      <input
        type="number"
        className="input"
        aria-label={label}
        step="any"
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </label>
  );
}

/** Parse a form string into a wire number-or-null ("" → null = inherit). */
function toNumberOrNull(raw: string): number | null {
  const t = raw.trim();
  if (t === '') return null;
  const n = Number(t);
  return Number.isFinite(n) ? n : null;
}

export function SettingsPage(): React.JSX.Element {
  const settingsQ = useSettings();
  const configQ = useConfig();
  const update = useUpdateInstrumentSettings();

  const overrides = useMemo(
    () => settingsQ.data?.instrumentOverrides ?? {},
    [settingsQ.data?.instrumentOverrides],
  );
  const global = settingsQ.data?.global;
  const available = settingsQ.data?.availableRequiredConditions ?? [];

  // Every symbol the operator can pick from the datalist: the catalogued instruments (FX majors + index),
  // the scanned symbols, and any that already carry an override. The control is editable too, so an
  // uncatalogued symbol can be typed (it still resolves via the FX-default fallback, validated on save).
  const symbols = useMemo(
    () =>
      [
        ...new Set([
          ...(settingsQ.data?.availableInstruments ?? []),
          ...(configQ.data?.symbols ?? []),
          ...Object.keys(overrides),
        ]),
      ].sort(),
    [settingsQ.data?.availableInstruments, configQ.data?.symbols, overrides],
  );

  // The dropdown selection; an empty string means "not yet chosen", resolved to the first symbol below
  // (derived — no effect, so editing is never clobbered by the reconcile poll). A successful save/clear
  // bumps `reloadNonce`, which is part of the form's key, so the editor remounts and re-reads the applied
  // override (a "fully uncontrolled component with a key" — the React-idiomatic reset).
  const [selected, setSelected] = useState('');
  const [reloadNonce, setReloadNonce] = useState(0);
  const activeSymbol = selected || symbols[0] || '';

  return (
    <div className="page page--settings">
      <section className="panel" aria-label="Per-instrument overrides">
        <header className="panel__head">
          <span>Per-instrument tuning</span>
          <span className="badge-advisory">Live · no restart</span>
        </header>
        <div className="panel__body">
          {settingsQ.isError ? (
            <p className="empty error" role="alert">
              Settings unavailable — {errorMessage(settingsQ.error)}
            </p>
          ) : symbols.length === 0 ? (
            <p className="empty">No symbols available to tune.</p>
          ) : (
            <>
              <label className="form__field" style={{ maxWidth: 240, marginBottom: 12 }}>
                <span>Symbol (pick or type)</span>
                <input
                  className="input"
                  aria-label="Symbol"
                  list="settings-symbols"
                  value={activeSymbol}
                  onChange={(e) => setSelected(e.target.value.toUpperCase())}
                />
                <datalist id="settings-symbols">
                  {symbols.map((s) => (
                    <option key={s} value={s}>
                      {s in overrides ? 'has override' : ''}
                    </option>
                  ))}
                </datalist>
              </label>

              <InstrumentOverrideForm
                key={`${activeSymbol}#${reloadNonce}`}
                symbol={activeSymbol}
                initial={overrides[activeSymbol]}
                available={available}
                hasOverride={activeSymbol in overrides}
                update={update}
                onMutated={() => setReloadNonce((n) => n + 1)}
              />
            </>
          )}

          {Object.keys(overrides).length > 0 ? (
            <table className="tbl" aria-label="Current overrides">
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>k/n</th>
                  <th>Required subset</th>
                  <th>Min stop</th>
                  <th>Spread</th>
                  <th>Commission</th>
                </tr>
              </thead>
              <tbody>
                {Object.entries(overrides).map(([sym, ov]) => (
                  <tr key={sym}>
                    <td className="num" style={{ fontWeight: 700 }}>
                      {sym}
                    </td>
                    <td className="num neutral">{ov.minRequiredConditions ?? '—'}</td>
                    <td className="num neutral" title={(ov.requiredConditions ?? []).join(', ')}>
                      {ov.requiredConditions ? `${ov.requiredConditions.length} of ${available.length}` : '—'}
                    </td>
                    <td className="num">{ov.minStopDistancePips ?? '—'}</td>
                    <td className="num">{ov.spreadBasePips ?? '—'}</td>
                    <td className="num">{ov.commissionPerLotRoundTripUsd ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
        </div>
      </section>

      <GlobalConceptCard global={global} loading={settingsQ.isLoading} />

      <EconomicCalendarCard />
    </div>
  );
}

/**
 * The economic-calendar status (§2.5.8): the source/loaded state, the upcoming FOMC/NFP/CPI events, and the §2.5.2
 * no-trade days the gate enforces. Read-only — it shows what the scanner blocks, with no order control.
 */
function EconomicCalendarCard(): React.JSX.Element {
  const calendarQ = useCalendar();
  const calendar: CalendarStatusDto | undefined = calendarQ.data;

  return (
    <section className="panel" aria-label="Economic calendar">
      <header className="panel__head">
        <span>Economic calendar</span>
        {calendar ? (
          <span className="num neutral">
            {calendar.enabled ? `${calendar.provider} · ${calendar.loaded ? 'loaded' : 'not loaded'}` : 'disabled'}
          </span>
        ) : null}
      </header>
      <div className="panel__body">
        {calendarQ.isError ? (
          <p className="empty error" role="alert">
            Calendar unavailable — {errorMessage(calendarQ.error)}
          </p>
        ) : !calendar ? (
          <p className="empty">{calendarQ.isLoading ? 'Loading…' : 'No calendar available.'}</p>
        ) : !calendar.enabled ? (
          <p className="empty">
            The calendar feed is disabled (<code>Ict:Calendar:Enabled=false</code>), so the §2.5.2 FOMC/NFP gate is
            fail-open — no day is blocked. Enable a source to enforce the no-trade days.
          </p>
        ) : (
          <>
            <div className="cfg__row">
              <span className="cfg__label">Window (NY)</span>
              <span className="cfg__value">
                {calendar.fromDate} → {calendar.toDate} · {calendar.blackoutDates.length} no-trade day(s)
              </span>
            </div>
            {calendar.events.length === 0 ? (
              <p className="empty">No FOMC/NFP/CPI events in the window.</p>
            ) : (
              <table className="tbl" aria-label="Economic events" style={{ marginTop: 8 }}>
                <thead>
                  <tr>
                    <th>Date (NY)</th>
                    <th>Event</th>
                    <th>No-trade</th>
                  </tr>
                </thead>
                <tbody>
                  {calendar.events.map((e) => (
                    <tr key={`${e.date}-${e.type}`}>
                      <td className="num">{e.date}</td>
                      <td>{e.type}</td>
                      <td className={`num ${e.isBlackout ? 'short' : 'long'}`}>{e.isBlackout ? 'blocked' : 'clear'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}
      </div>
    </section>
  );
}

/**
 * The editable override form for ONE symbol. It is keyed by the symbol (+ a reload nonce) in the parent,
 * so React remounts it on a symbol switch or after a save/clear — its state initializes from `initial`
 * via lazy useState initializers, with NO effect (the idiomatic uncontrolled-with-a-key reset).
 */
function InstrumentOverrideForm({
  symbol,
  initial,
  available,
  hasOverride,
  update,
  onMutated,
}: {
  symbol: string;
  initial: InstrumentSettingsDto | undefined;
  available: string[];
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
  const [htfBias, setHtfBias] = useState<boolean>(() => initial?.requireReferenceOpenAgreement ?? false);
  const [localError, setLocalError] = useState('');

  function toggleCondition(c: string): void {
    setSubset((prev) => {
      const next = new Set(prev);
      if (next.has(c)) next.delete(c);
      else next.add(c);
      return next;
    });
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
      // Checked = require the HTF daily-bias agreement for this symbol; unchecked = inherit the global default (off).
      requireReferenceOpenAgreement: htfBias ? true : null,
    };
    update.mutate({ symbol, body }, { onSuccess: onMutated });
  }

  function onClear(): void {
    setLocalError('');
    update.mutate({ symbol, body: null }, { onSuccess: onMutated });
  }

  const errorText = localError || (update.isError ? errorMessage(update.error) : '');

  return (
    <form className="form form--grid" onSubmit={onSave} aria-label="Instrument override form">
      <NumberField
        label="Min required (k of n)"
        value={minK}
        onChange={setMinK}
        placeholder="inherit (strict)"
      />

      <fieldset className="multi" aria-label="Required-condition subset">
        <legend>Required subset — none = inherit</legend>
        <div className="multi__opts">
          {available.map((c) => {
            const on = subset.has(c);
            const lock = c === DIRECTION_LOCK;
            return (
              <button
                key={c}
                type="button"
                className={`chip-toggle${on ? ' chip-toggle--on' : ''}`}
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
      </fieldset>

      <NumberField label="Min stop (pips)" value={minStop} onChange={setMinStop} />
      <NumberField label="Spread base (pips)" value={spread} onChange={setSpread} />
      <NumberField
        label="Commission ($/lot round-trip)"
        value={commission}
        onChange={setCommission}
      />

      <label className="form__field form__field--check">
        <input
          type="checkbox"
          checked={htfBias}
          onChange={(e) => setHtfBias(e.target.checked)}
        />
        <span title="Require the entry to agree with the day's reference-open bias (the HTF daily-bias filter) for this symbol. Unchecked inherits the global default.">
          Require HTF daily-bias agreement
        </span>
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
  );
}

/** The read-only global concept settings (the ICT model the scanner runs under). */
function GlobalConceptCard({
  global,
  loading,
}: {
  global: GlobalConceptSettingsDto | undefined;
  loading: boolean;
}): React.JSX.Element {
  return (
    <section className="panel" aria-label="Global concept settings">
      <header className="panel__head">
        <span>Concept settings (global)</span>
        <span className="num neutral">read-only · Ict:* config</span>
      </header>
      <div className="panel__body">
        {!global ? (
          <p className="empty">{loading ? 'Loading…' : 'No concept settings available.'}</p>
        ) : (
          <div className="settings__grid">
            <div className="settings__group" aria-label="Confluence and grading">
              <h3>Confluence &amp; grading</h3>
              <CfgRow label="Required conditions">
                {global.requiredConditions.length} ({global.requiredConditions.join(', ')})
              </CfgRow>
              <CfgRow label="Min required (k of n)">
                {global.minRequiredConditions ?? 'all (strict §2.5)'}
              </CfgRow>
              <CfgRow label="Grade A / B / C">
                {global.gradeAThreshold} / {global.gradeBThreshold} / {global.gradeCThreshold}
              </CfgRow>
              <CfgRow label="Alert floor">grade {global.alertMinimumGrade}</CfgRow>
              <table className="tbl" aria-label="Confluence weights" style={{ marginTop: 8 }}>
                <thead>
                  <tr>
                    <th>Condition</th>
                    <th>Weight</th>
                  </tr>
                </thead>
                <tbody>
                  {Object.entries(global.weights)
                    .sort((a, b) => b[1] - a[1])
                    .map(([cond, w]) => (
                      <tr key={cond}>
                        <td>{cond}</td>
                        <td className="num">{w.toFixed(2)}</td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>

            <div className="settings__group" aria-label="Risk">
              <h3>Risk (§2.4 / §2.5.5)</h3>
              <CfgRow label="Base risk">{formatPercentValue(global.baseRiskPercent)}</CfgRow>
              <CfgRow label="Portfolio cap">
                {formatPercentValue(global.maxOpenPortfolioRiskPercent)}
              </CfgRow>
              <CfgRow label="Hard max">{formatPercentValue(global.hardMaxRiskPercent)}</CfgRow>
              <CfgRow label="Min stop distance">{global.minStopDistancePips} pips</CfgRow>
              <CfgRow label="Loss ladder">
                {global.lossLadderPercents.map((p) => `${p}%`).join(' → ')}
              </CfgRow>
              <CfgRow label="Win-cycle">{global.consecutiveWinsForLowestUnit} wins → lowest unit</CfgRow>
              <CfgRow label="Dip recovery">{formatPct(global.dipRecoveryFraction)}</CfgRow>
            </div>

            <div className="settings__group" aria-label="Execution and scanning">
              <h3>Execution &amp; scanning</h3>
              <CfgRow label="Spread base">{global.spreadBasePips} pips</CfgRow>
              <CfgRow label="Commission">${global.commissionPerLotRoundTripUsd} / lot round-trip</CfgRow>
              <CfgRow label="Active killzones">{global.activeKillzones.join(', ') || '—'}</CfgRow>
              <CfgRow label="Active styles">{global.activeStyles.join(', ') || '—'}</CfgRow>
            </div>
          </div>
        )}
      </div>
    </section>
  );
}
