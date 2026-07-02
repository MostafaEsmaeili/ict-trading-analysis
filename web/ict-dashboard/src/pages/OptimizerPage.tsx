// ---------------------------------------------------------------------------------------------------
// OptimizerPage — the parameter-grid optimizer (plan §15 §6). A form (symbols / styles / timeframes
// multi-select, risk-% list, starting balance, period, objective, topN) runs POST /api/backtest/optimize
// via the useOptimize mutation. Renders a ranked leaderboard (rank, symbol, tf, style, risk%, trades,
// win%, avg R, profit factor, ending balance, score) — the score colored, the top row highlighted.
// Clicking a row deep-links to the Backtest Lab pre-filled with that combination.
//
// Read-only/advisory: the optimizer only RANKS simulated backtests; there is no execute path (§6.3).
// ---------------------------------------------------------------------------------------------------

import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useBacktestDatasets, useOptimize } from '../api/hooks';
import type { OptimizeRequest, OptimizerResultDto } from '../types/api';
import { UNDEFINED_PROFIT_FACTOR } from '../types/api';
import { STYLES } from '../components/ChartPanel';
import { MODELS, modelLabel } from '../models';
import { formatMoney, formatPct } from '../format';
import { errorMessage } from '../format-error';

const OBJECTIVES = ['Expectancy', 'ProfitFactor', 'AverageR', 'EndingBalance'] as const;
const RISK_CHOICES = [0.5, 1, 1.5, 2] as const;
// The "k of n" required-condition relaxation to sweep; none selected = strict all-8 §2.5 model only.
const MIN_REQUIRED_CHOICES = [5, 6, 7, 8] as const;

// The canonical §2.5.2 required set — used to show which concepts a subset DROPPED (made optional) in the leaderboard.
const ALL_REQUIRED = [
  'BiasAligned', 'KillzoneEntry', 'LiquiditySweep', 'DisplacementMss',
  'FvgPresent', 'PremiumDiscountHalf', 'DrawTargetRrMet', 'CalendarClear',
] as const;

/** The required conditions a row DROPPED to optional (empty = strict all-required). */
function droppedConditions(required: readonly string[] | null | undefined): string {
  if (!required || required.length === 0) return '—';
  const dropped = ALL_REQUIRED.filter((c) => !required.includes(c));
  return dropped.length === 0 ? '—' : dropped.map((c) => c.replace(/[a-z]/g, '')).join(' ');
}

/** A simple checkbox multi-select group. */
function MultiSelect({
  label,
  options,
  selected,
  onToggle,
  ariaLabel,
}: {
  label: string;
  options: readonly (string | number)[];
  selected: ReadonlySet<string>;
  onToggle: (value: string) => void;
  ariaLabel: string;
}): React.JSX.Element {
  return (
    <fieldset className="multi" aria-label={ariaLabel}>
      <legend>{label}</legend>
      <div className="multi__opts">
        {options.map((o) => {
          const v = String(o);
          const on = selected.has(v);
          return (
            <button
              key={v}
              type="button"
              className={`chip-toggle${on ? ' chip-toggle--on' : ''}`}
              aria-pressed={on}
              onClick={() => onToggle(v)}
            >
              {v}
            </button>
          );
        })}
      </div>
    </fieldset>
  );
}

function useToggleSet(initial: string[]): [Set<string>, (v: string) => void] {
  const [set, setSet] = useState<Set<string>>(new Set(initial));
  const toggle = (v: string): void => {
    setSet((prev) => {
      const next = new Set(prev);
      if (next.has(v)) next.delete(v);
      else next.add(v);
      return next;
    });
  };
  return [set, toggle];
}

function profitFactorLabel(pf: number): string {
  return pf >= UNDEFINED_PROFIT_FACTOR ? '∞' : pf.toFixed(2);
}

export function OptimizerPage(): React.JSX.Element {
  const navigate = useNavigate();
  const datasetsQ = useBacktestDatasets();
  const optimize = useOptimize();

  const symbolOptions = useMemo(
    () => [...new Set((datasetsQ.data ?? []).map((d) => d.symbol))],
    [datasetsQ.data],
  );
  const tfOptions = useMemo(
    () => [...new Set((datasetsQ.data ?? []).map((d) => d.timeframe))],
    [datasetsQ.data],
  );

  const [symbols, toggleSymbol] = useToggleSet([]);
  const [styles, toggleStyle] = useToggleSet(['Intraday']);
  const [timeframes, toggleTimeframe] = useToggleSet([]);
  const [risks, toggleRisk] = useToggleSet(['1']);
  const [models, toggleModel] = useToggleSet(['Ict2022']);
  const [minReqs, toggleMinReq] = useToggleSet([]);
  const [startingBalance, setStartingBalance] = useState(10000);
  const [objective, setObjective] = useState<(typeof OBJECTIVES)[number]>('Expectancy');
  const [topN, setTopN] = useState(10);
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [leaveOut, setLeaveOut] = useState(0);

  // Default the symbol selection to all available once datasets load (kept controlled but lazy).
  const effectiveSymbols = symbols.size > 0 ? [...symbols] : symbolOptions;
  const effectiveTimeframes = timeframes.size > 0 ? [...timeframes] : tfOptions;

  function onSubmit(e: React.FormEvent): void {
    e.preventDefault();
    const req: OptimizeRequest = {
      symbols: effectiveSymbols,
      styles: [...styles],
      riskPercents: [...risks].map(Number),
      timeframes: effectiveTimeframes,
      startingBalance,
      objective,
      topN,
      fromUtc: fromDate ? `${fromDate}T00:00:00Z` : undefined,
      toUtc: toDate ? `${toDate}T23:59:59Z` : undefined,
      minRequiredConditions: minReqs.size > 0 ? [...minReqs].map(Number) : undefined,
      leaveOutUpTo: leaveOut > 0 ? leaveOut : undefined,
      models: models.size > 0 ? [...models] : undefined,
    };
    optimize.mutate(req);
  }

  function drillIn(row: OptimizerResultDto): void {
    navigate(
      `/backtest?symbol=${encodeURIComponent(row.symbol)}&timeframe=${encodeURIComponent(
        row.timeframe,
      )}&style=${encodeURIComponent(row.style)}&risk=${row.riskPercent}&model=${encodeURIComponent(
        row.model,
      )}`,
    );
  }

  const result = optimize.data;

  return (
    <div className="page page--optimizer">
      <section className="panel" aria-label="Optimizer configuration">
        <header className="panel__head">
          <span>Optimizer</span>
          <span className="badge-advisory">Paper</span>
        </header>
        <div className="panel__body">
          <form className="form form--grid" onSubmit={onSubmit} aria-label="Optimizer form">
            <MultiSelect
              label="Symbols"
              ariaLabel="Symbols multi-select"
              options={symbolOptions}
              selected={symbols}
              onToggle={toggleSymbol}
            />
            <MultiSelect
              label="Styles"
              ariaLabel="Styles multi-select"
              options={STYLES}
              selected={styles}
              onToggle={toggleStyle}
            />
            <MultiSelect
              label="Models"
              ariaLabel="Models multi-select"
              options={MODELS.map((m) => m.value)}
              selected={models}
              onToggle={toggleModel}
            />
            <MultiSelect
              label="Timeframes"
              ariaLabel="Timeframes multi-select"
              options={tfOptions}
              selected={timeframes}
              onToggle={toggleTimeframe}
            />
            <MultiSelect
              label="Risk %"
              ariaLabel="Risk percent multi-select"
              options={RISK_CHOICES}
              selected={risks}
              onToggle={toggleRisk}
            />
            <MultiSelect
              label="Min required (k of n) — none = strict 8"
              ariaLabel="Min required conditions multi-select"
              options={MIN_REQUIRED_CHOICES}
              selected={minReqs}
              onToggle={toggleMinReq}
            />

            <label className="form__field">
              <span>Starting balance</span>
              <input
                type="number"
                className="input"
                aria-label="Starting balance"
                min={1}
                step="any"
                value={startingBalance}
                onChange={(e) => setStartingBalance(Number(e.target.value))}
              />
            </label>

            <label className="form__field">
              <span>From</span>
              <input
                type="date"
                className="input"
                aria-label="From date"
                value={fromDate}
                onChange={(e) => setFromDate(e.target.value)}
              />
            </label>

            <label className="form__field">
              <span>To</span>
              <input
                type="date"
                className="input"
                aria-label="To date"
                value={toDate}
                onChange={(e) => setToDate(e.target.value)}
              />
            </label>

            <label className="form__field">
              <span>Drop up to (subset search)</span>
              <input
                type="number"
                className="input"
                aria-label="Leave out up to"
                min={0}
                max={3}
                step={1}
                value={leaveOut}
                onChange={(e) => setLeaveOut(Number(e.target.value))}
              />
            </label>

            <label className="form__field">
              <span>Objective</span>
              <select
                className="input"
                aria-label="Objective"
                value={objective}
                onChange={(e) => setObjective(e.target.value as (typeof OBJECTIVES)[number])}
              >
                {OBJECTIVES.map((o) => (
                  <option key={o} value={o}>
                    {o}
                  </option>
                ))}
              </select>
            </label>

            <label className="form__field">
              <span>Top N</span>
              <input
                type="number"
                className="input"
                aria-label="Top N"
                min={1}
                max={100}
                step={1}
                value={topN}
                onChange={(e) => setTopN(Number(e.target.value))}
              />
            </label>

            <div className="form__actions">
              <button
                type="submit"
                className="btn btn--primary"
                disabled={optimize.isPending || styles.size === 0 || risks.size === 0}
              >
                {optimize.isPending ? 'Optimizing…' : 'Optimize'}
              </button>
            </div>
          </form>

          {optimize.isError ? (
            <p className="empty error" role="alert">
              Optimize failed — {errorMessage(optimize.error)}
            </p>
          ) : null}
        </div>
      </section>

      <section className="panel" aria-label="Optimizer leaderboard">
        <header className="panel__head">
          <span>Leaderboard</span>
          {result ? (
            <span className="num neutral">
              {result.results.length}/{result.combinationCount} · obj {result.objective}
            </span>
          ) : null}
        </header>
        <div className="panel__body panel__body--flush">
          {optimize.isPending ? (
            <p className="empty">Running grid…</p>
          ) : !result ? (
            <p className="empty">Configure a grid and press “Optimize” to rank combinations.</p>
          ) : result.results.length === 0 ? (
            <p className="empty">No combinations produced a result.</p>
          ) : (
            <table className="tbl tbl--leaderboard" aria-label="Leaderboard">
              <thead>
                <tr>
                  <th>Rank</th>
                  <th>Symbol</th>
                  <th>TF</th>
                  <th>Style</th>
                  <th>Model</th>
                  <th>Risk %</th>
                  <th>k/n</th>
                  <th>Dropped</th>
                  <th>Trades</th>
                  <th>Win %</th>
                  <th>Avg R</th>
                  <th>PF</th>
                  <th>Ending</th>
                  <th>Score</th>
                </tr>
              </thead>
              <tbody>
                {result.results.map((row, i) => (
                  <tr
                    key={`${row.symbol}-${row.timeframe}-${row.style}-${row.model}-${row.riskPercent}-${row.minRequiredConditions ?? 'all'}-${(row.requiredConditions ?? []).join('+')}`}
                    className={i === 0 ? 'row--top' : undefined}
                    role="button"
                    tabIndex={0}
                    aria-label={`Open ${row.symbol} ${row.timeframe} ${row.style} in backtest`}
                    onClick={() => drillIn(row)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        drillIn(row);
                      }
                    }}
                    style={{ cursor: 'pointer' }}
                  >
                    <td className="num neutral">{i + 1}</td>
                    <td className="num" style={{ fontWeight: 700 }}>
                      {row.symbol}
                    </td>
                    <td className="num">{row.timeframe}</td>
                    <td>{row.style}</td>
                    <td className="num neutral" title={modelLabel(row.model)}>{row.model.replace('Ict', '')}</td>
                    <td className="num">{row.riskPercent}</td>
                    <td className="num neutral">{row.minRequiredConditions ?? 'all'}</td>
                    <td className="num neutral" title={(row.requiredConditions ?? []).join(', ')}>
                      {droppedConditions(row.requiredConditions)}
                    </td>
                    <td className="num">{row.tradeCount}</td>
                    <td className="num">{formatPct(row.winRate)}</td>
                    <td className={`num ${row.averageR >= 0 ? 'long' : 'short'}`}>
                      {row.averageR.toFixed(2)}R
                    </td>
                    <td className="num">{profitFactorLabel(row.profitFactor)}</td>
                    <td
                      className={`num ${row.endingBalance >= startingBalance ? 'long' : 'short'}`}
                    >
                      {formatMoney(row.endingBalance)}
                    </td>
                    <td className={`num score ${row.score >= 0 ? 'long' : 'short'}`}>
                      {row.score.toFixed(3)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </section>
    </div>
  );
}
