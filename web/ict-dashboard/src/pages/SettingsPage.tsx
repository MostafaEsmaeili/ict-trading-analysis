// ---------------------------------------------------------------------------------------------------
// SettingsPage — the operator's live settings, reorganised for a non-expert (operator complaint: "the
// setting part is hard to understand"). It is now a TAB ORCHESTRATOR over five tabs, plus a global
// Simple ⇄ Advanced toggle (persisted to localStorage; Simple hides raw weights / hard-max / dip-recovery):
//
//   • Quick setup    — pick a symbol, apply a preset, tune it, Save. The friendly default landing tab.
//   • Instruments    — the same per-instrument editor + the table of every current override.
//   • Concept model  — the read-only global ICT model, explained in groups with InfoTips.
//   • Discovery      — a preview of the scan matrix + a LIVE per-instrument Auto/Manual entry-mode control.
//   • Calendar       — the economic-calendar (FOMC/NFP) no-trade-day status.
//
// The WIRE CONTRACT IS UNCHANGED — this is a pure IA/UX rework over the existing SettingsDto /
// InstrumentSettingsDto / GET /api/settings / PUT /api/settings/instruments/{symbol}. Read-only/advisory:
// changing settings only tunes what the scanner CONSIDERS — there is no order/execute path (§6.3).
// ---------------------------------------------------------------------------------------------------

import { useMemo, useState } from 'react';
import { useConfig, useSettings, useUpdateInstrumentSettings } from '../api/hooks';
import { useLocalStorageState } from '../hooks/useLocalStorageState';
import { errorMessage } from '../format-error';
import { InstrumentOverrideForm } from '../components/settings/InstrumentOverrideForm';
import { GlobalConceptCard } from '../components/settings/GlobalConceptCard';
import { EconomicCalendarCard } from '../components/settings/EconomicCalendarCard';
import { DiscoveryPanel } from '../components/settings/DiscoveryPanel';

type TabId = 'quick' | 'instruments' | 'concept' | 'discovery' | 'calendar';

const TABS: readonly { id: TabId; label: string }[] = [
  { id: 'quick', label: 'Quick setup' },
  { id: 'instruments', label: 'Instruments' },
  { id: 'concept', label: 'Concept model' },
  { id: 'discovery', label: 'Discovery' },
  { id: 'calendar', label: 'Calendar' },
];

export function SettingsPage(): React.JSX.Element {
  const [tab, setTab] = useState<TabId>('quick');
  const [advanced, setAdvanced] = useLocalStorageState<boolean>('ict.settings.advanced', false);

  return (
    <div className="page page--settings">
      <section className="panel settings-shell" aria-label="Settings">
        <header className="panel__head settings-shell__head">
          <div className="seg" role="tablist" aria-label="Settings sections">
            {TABS.map((t) => (
              <button
                key={t.id}
                type="button"
                role="tab"
                aria-selected={tab === t.id}
                aria-pressed={tab === t.id}
                onClick={() => setTab(t.id)}
              >
                {t.label}
              </button>
            ))}
          </div>

          <label className="mode-toggle" title="Simple hides the raw weights and deeper risk knobs">
            <span className="mode-toggle__label">{advanced ? 'Advanced' : 'Simple'}</span>
            <button
              type="button"
              role="switch"
              aria-checked={advanced}
              aria-label="Advanced mode"
              className={`switch${advanced ? ' switch--on' : ''}`}
              onClick={() => setAdvanced(!advanced)}
            >
              <span className="switch__knob" />
            </button>
          </label>
        </header>
      </section>

      {tab === 'quick' || tab === 'instruments' ? (
        <InstrumentTuning showAllOverrides={tab === 'instruments'} />
      ) : null}

      {tab === 'concept' ? <ConceptTab advanced={advanced} /> : null}

      {tab === 'discovery' ? <DiscoveryPanel /> : null}

      {tab === 'calendar' ? <EconomicCalendarCard /> : null}
    </div>
  );
}

/** The concept-model tab — just the read-only global card (advanced reveals the raw weights). */
function ConceptTab({ advanced }: { advanced: boolean }): React.JSX.Element {
  const settingsQ = useSettings();
  return <GlobalConceptCard global={settingsQ.data?.global} loading={settingsQ.isLoading} advanced={advanced} />;
}

/**
 * The per-instrument tuning surface, shared by the Quick-setup and Instruments tabs. Quick setup is the
 * editor alone; Instruments adds the table of every current override beneath it.
 */
function InstrumentTuning({ showAllOverrides }: { showAllOverrides: boolean }): React.JSX.Element {
  const settingsQ = useSettings();
  const configQ = useConfig();
  const update = useUpdateInstrumentSettings();

  const overrides = useMemo(
    () => settingsQ.data?.instrumentOverrides ?? {},
    [settingsQ.data?.instrumentOverrides],
  );
  const global = settingsQ.data?.global;
  const available = settingsQ.data?.availableRequiredConditions ?? [];

  // Every symbol the operator can pick: catalogued instruments + scanned symbols + any with an override.
  // The control is editable too, so an uncatalogued symbol can be typed (validated on save).
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

  // The dropdown selection; "" = not yet chosen, resolved to the first symbol (derived — no effect, so an
  // in-progress edit is never clobbered by the reconcile poll). A save/clear bumps reloadNonce → the
  // editor remounts and re-reads the applied override (the fully-uncontrolled-with-a-key reset).
  const [selected, setSelected] = useState('');
  const [reloadNonce, setReloadNonce] = useState(0);
  const activeSymbol = selected || symbols[0] || '';

  return (
    <>
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
              <label className="form__field" style={{ maxWidth: 260, marginBottom: 12 }}>
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
                global={global}
                hasOverride={activeSymbol in overrides}
                update={update}
                onMutated={() => setReloadNonce((n) => n + 1)}
              />
            </>
          )}
        </div>
      </section>

      {showAllOverrides && Object.keys(overrides).length > 0 ? (
        <section className="panel" aria-label="Current overrides">
          <header className="panel__head">
            <span>Current overrides</span>
            <span className="num neutral">{Object.keys(overrides).length} symbol(s)</span>
          </header>
          <div className="panel__body">
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
          </div>
        </section>
      ) : null}
    </>
  );
}
