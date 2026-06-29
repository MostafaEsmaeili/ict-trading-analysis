// ---------------------------------------------------------------------------------------------------
// GlobalConceptCard — the read-only "Concept model" view (the ICT model the scanner runs under), bound
// from Ict:* at startup. Reorganised from the old flat dump into explained GROUPS (gates / confluence /
// grading / risk / execution) each with a plain-language one-liner + InfoTips, so a non-expert can read
// what the model does. The raw per-condition weights table is Advanced-only (hidden in Simple mode).
//
// READ-ONLY: the live-editable surface is the per-instrument override; this card never changes settings
// and carries no order/execute control (§6.3).
// ---------------------------------------------------------------------------------------------------

import type { GlobalConceptSettingsDto } from '../../types/api';
import { formatPct, formatPercentValue } from '../../format';
import { InfoTip } from '../InfoTip';

function CfgRow({
  label,
  term,
  children,
}: {
  label: string;
  term?: React.ComponentProps<typeof InfoTip>['term'];
  children: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className="cfg__row">
      <span className="cfg__label">
        {label}
        {term ? <InfoTip term={term} /> : null}
      </span>
      <span className="cfg__value">{children}</span>
    </div>
  );
}

/** One explained group of settings (a heading + a plain one-liner + rows). */
function Group({
  title,
  blurb,
  children,
  label,
}: {
  title: string;
  blurb: string;
  children: React.ReactNode;
  label: string;
}): React.JSX.Element {
  return (
    <div className="concept-group" aria-label={label}>
      <h3>{title}</h3>
      <p className="concept-group__blurb">{blurb}</p>
      {children}
    </div>
  );
}

export function GlobalConceptCard({
  global,
  loading,
  advanced,
}: {
  global: GlobalConceptSettingsDto | undefined;
  loading: boolean;
  /** Advanced mode reveals the raw per-condition weights + the deeper risk knobs. */
  advanced: boolean;
}): React.JSX.Element {
  return (
    <section className="panel" aria-label="Global concept settings">
      <header className="panel__head">
        <span>Concept model (global)</span>
        <span className="num neutral">read-only · Ict:* config</span>
      </header>
      <div className="panel__body">
        {!global ? (
          <p className="empty">{loading ? 'Loading…' : 'No concept settings available.'}</p>
        ) : (
          <div className="settings__grid">
            <Group
              title="Gates — what must line up"
              label="Confluence and grading"
              blurb="The fixed set of checks the scanner stacks up. Each agreeing reason raises the quality."
            >
              <CfgRow label="Confluence" term="confluence">
                <span className="num">{global.requiredConditions.length}</span> required checks
              </CfgRow>
              <CfgRow label="Required (k of n)" term="kOfN">
                {global.minRequiredConditions ?? 'all (strict §2.5)'}
              </CfgRow>
              <CfgRow label="Required set">
                <span className="concept-tags">
                  {global.requiredConditions.map((c) => (
                    <span key={c} className="concept-tag">
                      {c}
                    </span>
                  ))}
                </span>
              </CfgRow>
            </Group>

            <Group
              title="Grading — how signals are scored"
              label="Grading"
              blurb="Each signal gets a 0–100 score, then a letter grade. Only A and B fire by default."
            >
              <CfgRow label="Grade A / B / C" term="gradeThresholds">
                <span className="num">
                  {global.gradeAThreshold} / {global.gradeBThreshold} / {global.gradeCThreshold}
                </span>
              </CfgRow>
              <CfgRow label="Alert floor" term="alertFloor">
                grade {global.alertMinimumGrade}
              </CfgRow>
            </Group>

            <Group
              title="Risk — how much is at stake"
              label="Risk"
              blurb="How the simulator sizes each paper trade and caps total exposure (§2.4 / §2.5.5)."
            >
              <CfgRow label="Base risk" term="riskPerTrade">
                {formatPercentValue(global.baseRiskPercent)}
              </CfgRow>
              <CfgRow label="Portfolio cap" term="portfolioCap">
                {formatPercentValue(global.maxOpenPortfolioRiskPercent)}
              </CfgRow>
              <CfgRow label="Min stop distance" term="minStop">
                {global.minStopDistancePips} pips
              </CfgRow>
              <CfgRow label="Loss ladder">
                {global.lossLadderPercents.map((p) => `${p}%`).join(' → ')}
              </CfgRow>
              {advanced ? (
                <>
                  <CfgRow label="Hard max">{formatPercentValue(global.hardMaxRiskPercent)}</CfgRow>
                  <CfgRow label="Win-cycle">
                    {global.consecutiveWinsForLowestUnit} wins → lowest unit
                  </CfgRow>
                  <CfgRow label="Dip recovery">{formatPct(global.dipRecoveryFraction)}</CfgRow>
                </>
              ) : null}
            </Group>

            <Group
              title="Execution & scanning"
              label="Execution and scanning"
              blurb="The trading costs subtracted from paper results, and where/when the scanner hunts."
            >
              <CfgRow label="Spread base" term="spreadCommission">
                {global.spreadBasePips} pips
              </CfgRow>
              <CfgRow label="Commission">${global.commissionPerLotRoundTripUsd} / lot round-trip</CfgRow>
              <CfgRow label="Active killzones" term="killzone">
                {global.activeKillzones.join(', ') || '—'}
              </CfgRow>
              <CfgRow label="Active styles">{global.activeStyles.join(', ') || '—'}</CfgRow>
            </Group>

            {advanced ? (
              <div className="concept-group concept-group--wide" aria-label="Confluence weights detail">
                <h3>
                  Confluence weights (advanced)
                  <InfoTip
                    title="Weights"
                    label="Help: confluence weights"
                  >
                    Each check contributes its weight to the 0–100 score. Higher-weight checks move the
                    grade more. The total weighted universe is fixed (§2.5.3).
                  </InfoTip>
                </h3>
                <p className="concept-group__blurb">
                  The raw contribution of every check to a signal’s score — sorted strongest first.
                </p>
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
            ) : null}
          </div>
        )}
      </div>
    </section>
  );
}
