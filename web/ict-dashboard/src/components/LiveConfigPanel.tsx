// ---------------------------------------------------------------------------------------------------
// LiveConfigPanel (plan §15 §3) — a compact card on the Live page showing the runtime configuration
// (provider, scanned symbols, active styles/killzones, base/max risk %, the §5.4 cost model) AND the
// live paper-account snapshot (starting balance, current equity + Δ%, open-risk-vs-cap bar, win/loss
// streaks, peak/drawdown). Read-only: NO deposit/withdraw/execute control (§6.3).
// ---------------------------------------------------------------------------------------------------

import type { AccountStatusDto, ConfigStatusDto, Killzone, TradeStyle } from '../types/api';
import {
  deltaPercent,
  formatMoney,
  formatPercentValue,
} from '../format';
import { errorMessage } from '../format-error';
import { KillzoneBadge, StyleChip } from './Badges';

export interface LiveConfigPanelProps {
  config: ConfigStatusDto | undefined;
  account: AccountStatusDto | undefined;
  isLoading: boolean;
  isError?: boolean;
  error?: unknown;
}

export function LiveConfigPanel({
  config,
  account,
  isLoading,
  isError,
  error,
}: LiveConfigPanelProps): React.JSX.Element {
  return (
    <section className="panel" aria-label="Account and configuration">
      <header className="panel__head">
        <span>Account &amp; Config</span>
        {config ? <span className="chip chip--style">{config.provider}</span> : null}
      </header>
      <div className="panel__body">
        {isError ? (
          <p className="empty error" role="alert">
            Account/config unavailable — {errorMessage(error)}
          </p>
        ) : isLoading || !config || !account ? (
          <p className="empty">Loading account…</p>
        ) : (
          <>
            {/* Account equity + delta */}
            <div className="acct-equity">
              <div>
                <div className="metric__label">Equity</div>
                <div className="num acct-equity__value">
                  {formatMoney(account.equity)}
                  <EquityDelta equity={account.equity} starting={account.startingEquity} />
                </div>
                <div className="acct-sub num">
                  start {formatMoney(account.startingEquity)}
                </div>
              </div>
              <div style={{ textAlign: 'right' }}>
                <div className="metric__label">Open trades</div>
                <div className="num acct-equity__value">{account.openTradeCount}</div>
              </div>
            </div>

            {/* Open-risk-vs-cap bar */}
            <div className="risk-bar" aria-label="Open risk vs cap">
              <div className="risk-bar__head">
                <span className="metric__label">Open risk</span>
                <span className="num">
                  {formatMoney(account.openRisk)} / {formatMoney(account.openRiskCap)} ·{' '}
                  {formatPercentValue(account.riskUtilizationPercent)}
                </span>
              </div>
              <div className="risk-bar__track">
                <div
                  className={`risk-bar__fill${account.riskUtilizationPercent >= 90 ? ' risk-bar__fill--hot' : ''}`}
                  style={{ width: `${Math.min(100, account.riskUtilizationPercent)}%` }}
                />
              </div>
              <div className="acct-sub num">
                cap {formatPercentValue(account.maxOpenPortfolioRiskPercent, 1)} of portfolio
              </div>
            </div>

            {/* Streaks + peak/drawdown */}
            <div className="acct-grid">
              <Stat label="Win streak" value={`${account.consecutiveWins}`} tone="long" />
              <Stat label="Loss streak" value={`${account.consecutiveLosses}`} tone="short" />
              <Stat label="Peak" value={formatMoney(account.peakEquity)} />
              <Stat label="Trough" value={formatMoney(account.drawdownTrough)} />
            </div>

            {/* Configuration */}
            <div className="cfg">
              <CfgRow label="Symbols">
                <span className="num">{config.symbols.join(', ')}</span>
              </CfgRow>
              <CfgRow label="Styles">
                <span className="chip-row">
                  {config.activeStyles.map((s) => (
                    <StyleChip key={s} style={s as TradeStyle} />
                  ))}
                </span>
              </CfgRow>
              <CfgRow label="Killzones">
                <span className="chip-row">
                  {config.activeKillzones.map((k) => (
                    <KillzoneBadge key={k} killzone={k as Killzone} />
                  ))}
                </span>
              </CfgRow>
              <CfgRow label="Risk">
                <span className="num">
                  base {formatPercentValue(config.baseRiskPercent)} · max{' '}
                  {formatPercentValue(config.maxOpenPortfolioRiskPercent)}
                </span>
              </CfgRow>
              <CfgRow label="Costs">
                <span className="num">
                  spread {config.spreadBasePips.toFixed(1)} pips · comm{' '}
                  {formatMoney(config.commissionPerLotRoundTripUsd)}/lot
                </span>
              </CfgRow>
            </div>
          </>
        )}
      </div>
    </section>
  );
}

function EquityDelta({ equity, starting }: { equity: number; starting: number }): React.JSX.Element {
  const d = deltaPercent(equity, starting);
  const tone = d > 0 ? 'long' : d < 0 ? 'short' : 'neutral';
  const sign = d > 0 ? '+' : '';
  return (
    <span className={`acct-delta ${tone}`}>
      {sign}
      {d.toFixed(2)}%
    </span>
  );
}

function Stat({
  label,
  value,
  tone,
}: {
  label: string;
  value: string;
  tone?: 'long' | 'short';
}): React.JSX.Element {
  return (
    <div className="metric">
      <div className="metric__label">{label}</div>
      <div className={`metric__value${tone ? ` ${tone}` : ''}`}>{value}</div>
    </div>
  );
}

function CfgRow({ label, children }: { label: string; children: React.ReactNode }): React.JSX.Element {
  return (
    <div className="cfg__row">
      <span className="cfg__label">{label}</span>
      <span className="cfg__value">{children}</span>
    </div>
  );
}
