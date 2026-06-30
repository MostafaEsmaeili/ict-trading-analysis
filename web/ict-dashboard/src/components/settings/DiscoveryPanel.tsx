// ---------------------------------------------------------------------------------------------------
// DiscoveryPanel — a PREVIEW of "discovery mode": which assets × timeframes × styles the scanner hunts,
// and an Auto/Manual entry-mode concept. It surfaces the idea so a non-expert understands how to widen
// the model to surface MORE setups to follow (per the research: more instruments + lower timeframes, not
// looser confluence) — while staying honest about what is editable today.
//
// The scan-selection (which assets/timeframes/styles to hunt) write endpoint does NOT exist yet, so the
// scan MATRIX stays a read-only PREVIEW backed by ConfigStatusDto. The per-instrument Auto/Manual ENTRY
// MODE, however, IS now a real wire field (InstrumentSettingsDto.entryMode) served by the existing
// PUT /api/settings/instruments/{symbol} — so this panel turns the old Auto/Manual preview into a LIVE,
// per-instrument 3-state control (inherit / Auto / Manual) via useUpdateInstrumentSettings.
//
// Read-only/advisory: changing the entry mode only decides WHO opens the PAPER trade (the engine vs the
// operator's Take button) — there is no order/execute control anywhere (§6.3).

import { useMemo, useState } from 'react';
import { useConfig, useSettings, useUpdateInstrumentSettings } from '../../api/hooks';
import type { ConfigStatusDto, InstrumentSettingsDto } from '../../types/api';
import { SYMBOLS, TIMEFRAMES, STYLES } from '../ChartPanel';
import { errorMessage } from '../../format-error';
import { InfoTip } from '../InfoTip';

/** A read-only matrix row: a dimension (assets/timeframes/styles) and which values are currently active. */
function MatrixRow({
  label,
  term,
  all,
  active,
}: {
  label: string;
  term?: React.ComponentProps<typeof InfoTip>['term'];
  all: readonly string[];
  active: ReadonlySet<string>;
}): React.JSX.Element {
  return (
    <div className="disco-row">
      <span className="disco-row__label">
        {label}
        {term ? <InfoTip term={term} /> : null}
      </span>
      <span className="disco-row__chips">
        {all.map((v) => {
          const on = active.has(v);
          return (
            <span key={v} className={`disco-chip${on ? ' disco-chip--on' : ''}`} title={on ? 'Currently scanning' : 'Not scanning'}>
              {v}
            </span>
          );
        })}
      </span>
    </div>
  );
}

export function DiscoveryPanel(): React.JSX.Element {
  const configQ = useConfig();
  const config: ConfigStatusDto | undefined = configQ.data;
  const settingsQ = useSettings();
  const update = useUpdateInstrumentSettings();

  // The currently-scanned matrix, projected from ConfigStatusDto (the only live source today).
  const activeSymbols = new Set(config?.symbols ?? []);
  const activeStyles = new Set(config?.activeStyles ?? []);
  // Timeframes are not on ConfigStatusDto; show the chart's known set with none pre-active (preview only).
  const activeTimeframes = new Set<string>();

  // The per-instrument entry-mode editor (LIVE). Pick a symbol, then inherit / Auto / Manual.
  const overrides = useMemo(
    () => settingsQ.data?.instrumentOverrides ?? {},
    [settingsQ.data?.instrumentOverrides],
  );
  const symbols = useMemo(
    () =>
      [
        ...new Set([
          ...(settingsQ.data?.availableInstruments ?? []),
          ...(config?.symbols ?? []),
          ...Object.keys(overrides),
        ]),
      ].sort(),
    [settingsQ.data?.availableInstruments, config?.symbols, overrides],
  );
  const [emSymbol, setEmSymbol] = useState('');
  const activeEmSymbol = emSymbol || symbols[0] || '';
  const currentMode: 'inherit' | 'Auto' | 'Manual' = (() => {
    const m = overrides[activeEmSymbol]?.entryMode;
    return m === 'Auto' || m === 'Manual' ? m : 'inherit';
  })();

  // Save the entry mode by MERGING it onto the symbol's existing override (so it never drops the other
  // fields). inherit → null on the entryMode field (use the global default).
  function saveEntryMode(next: 'inherit' | 'Auto' | 'Manual'): void {
    const existing = overrides[activeEmSymbol] ?? {};
    const body: InstrumentSettingsDto = {
      ...existing,
      entryMode: next === 'inherit' ? null : next,
    };
    update.mutate({ symbol: activeEmSymbol, body });
  }

  return (
    <div className="page page--settings">
      <section className="panel" aria-label="Discovery scan matrix">
        <header className="panel__head">
          <span>Discovery — find more setups</span>
          <span className="badge-advisory">Preview</span>
        </header>
        <div className="panel__body">
          {configQ.isError ? (
            <p className="empty error" role="alert">
              Config unavailable — {errorMessage(configQ.error)}
            </p>
          ) : (
            <>
              <p className="concept-group__blurb" style={{ marginTop: 0 }}>
                The strict model fires only ~1–2 high-quality setups per week per instrument. To see a
                fuller stream to follow, widen the scan across more <strong>assets</strong>, lower{' '}
                <strong>timeframes</strong>, and relax the gate per instrument on the{' '}
                <strong>Instruments</strong> tab. This is the canonical lever — more coverage, not a looser
                model.
              </p>

              <div className="disco-matrix" role="group" aria-label="Scan matrix (read-only preview)">
                <MatrixRow label="Assets" all={SYMBOLS} active={activeSymbols} />
                <MatrixRow label="Timeframes" all={TIMEFRAMES} active={activeTimeframes} />
                <MatrixRow label="Styles" all={STYLES} active={activeStyles} />
                <MatrixRow
                  label="Killzones"
                  term="killzone"
                  all={['Asian', 'LondonOpen', 'NewYorkOpen', 'LondonClose']}
                  active={new Set(config?.activeKillzones ?? [])}
                />
              </div>

              <p className="notice notice--info disco-note" role="note">
                Live editing of the scan matrix lands with the discovery endpoint. Today this reflects the
                running configuration (provider <strong>{config?.provider ?? '—'}</strong>); to change the
                hunted assets/styles, set <code>Ict:Scanning</code> in the host config.
              </p>
            </>
          )}
        </div>
      </section>

      <section className="panel" aria-label="Entry mode">
        <header className="panel__head">
          <span>Entry mode (Auto / Manual)</span>
          <span className="badge-advisory">Live · no restart</span>
        </header>
        <div className="panel__body">
          <p className="concept-group__blurb" style={{ marginTop: 0 }}>
            How a confirmed setup turns into a paper trade — set it per instrument:
          </p>
          <div className="entrymode" role="group" aria-label="Entry mode options">
            <div className={`entrymode__opt${currentMode === 'Auto' ? ' entrymode__opt--on' : ''}`}>
              <span className="entrymode__name">Auto</span>
              <span className="entrymode__desc">
                The engine arms/opens the paper trade automatically when a setup confirms.
              </span>
            </div>
            <div className={`entrymode__opt${currentMode === 'Manual' ? ' entrymode__opt--on' : ''}`}>
              <span className="entrymode__name">Manual</span>
              <span className="entrymode__desc">
                The setup is only ranked/alerted on the Signals page; you take it with the Take button.
                (Still paper — there is never a live order.)
              </span>
            </div>
          </div>

          {settingsQ.isError ? (
            <p className="empty error" role="alert">
              Settings unavailable — {errorMessage(settingsQ.error)}
            </p>
          ) : symbols.length === 0 ? (
            <p className="empty">No symbols available to tune.</p>
          ) : (
            <div className="form" style={{ marginTop: 12 }}>
              <label className="form__field" style={{ maxWidth: 220 }}>
                <span>Instrument</span>
                <select
                  className="input"
                  aria-label="Entry-mode instrument"
                  value={activeEmSymbol}
                  onChange={(e) => setEmSymbol(e.target.value)}
                >
                  {symbols.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              </label>
              <label className="form__field" style={{ maxWidth: 240 }}>
                <span>
                  Entry mode
                  <InfoTip term="entryMode" />
                </span>
                <select
                  className="input"
                  aria-label="Entry mode"
                  value={currentMode}
                  disabled={update.isPending}
                  onChange={(e) => saveEntryMode(e.target.value as 'inherit' | 'Auto' | 'Manual')}
                >
                  <option value="inherit">Inherit global default</option>
                  <option value="Auto">Auto — engine opens it</option>
                  <option value="Manual">Manual — you take it</option>
                </select>
              </label>
            </div>
          )}

          {update.isError ? (
            <p className="empty error" role="alert">
              {errorMessage(update.error)}
            </p>
          ) : null}

          <p className="notice notice--info disco-note" role="note">
            The defensive guardrail is unchanged either way — there is no execute/buy/sell control anywhere;
            Manual just means you open the PAPER trade yourself from the Signals page.
          </p>
        </div>
      </section>
    </div>
  );
}
