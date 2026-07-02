// ---------------------------------------------------------------------------------------------------
// ActiveModelsCard — the LIVE "Active setup models" control (plan §9 multi-model support). The backend
// added a setup-model dimension (ICT 2022 / ICT 2024); this toggles which model(s) the scanner runs live
// via PUT /api/settings/scanning, reading the current selection from GET /api/settings.activeModels and
// its option set from .availableModels (falling back to the MODELS const). At least one model must stay
// selected (the last-on chip disables its un-check). Applies immediately — no restart.
//
// Read-only/advisory: choosing models only tunes WHAT the scanner considers — there is no order/execute
// path anywhere (§6.3).
// ---------------------------------------------------------------------------------------------------

import { useSettings, useUpdateScanningSettings } from '../../api/hooks';
import { MODELS, modelLabel } from '../../models';
import { errorMessage } from '../../format-error';

export function ActiveModelsCard(): React.JSX.Element {
  const settingsQ = useSettings();
  const update = useUpdateScanningSettings();

  const available = settingsQ.data?.availableModels ?? MODELS.map((m) => m.value);
  const active = settingsQ.data?.activeModels ?? [];

  function toggle(model: string): void {
    const isOn = active.includes(model);
    // Keep at least one model selected — never let the operator clear the whole scanner.
    if (isOn && active.length <= 1) return;
    const next = isOn ? active.filter((m) => m !== model) : [...active, model];
    update.mutate({ activeModels: next });
  }

  return (
    <section className="panel" aria-label="Active setup models">
      <header className="panel__head">
        <span>Active setup models</span>
        <span className="badge-advisory">Live · no restart</span>
      </header>
      <div className="panel__body">
        {settingsQ.isError ? (
          <p className="empty error" role="alert">
            Settings unavailable — {errorMessage(settingsQ.error)}
          </p>
        ) : (
          <>
            <div className="multi__opts" role="group" aria-label="Setup models">
              {available.map((m) => {
                const on = active.includes(m);
                const lastOne = on && active.length <= 1;
                return (
                  <button
                    key={m}
                    type="button"
                    className={`chip-toggle${on ? ' chip-toggle--on' : ''}`}
                    aria-pressed={on}
                    disabled={update.isPending || lastOne}
                    title={lastOne ? 'At least one model must stay selected' : undefined}
                    onClick={() => toggle(m)}
                  >
                    {modelLabel(m)}
                  </button>
                );
              })}
            </div>
            <p className="concept-group__blurb" style={{ marginTop: 8 }}>
              Models the scanner runs live — applies immediately, no restart.
            </p>
            {update.isError ? (
              <p className="empty error" role="alert">
                {errorMessage(update.error)}
              </p>
            ) : null}
          </>
        )}
      </div>
    </section>
  );
}
