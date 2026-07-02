// ---------------------------------------------------------------------------------------------------
// BacktestPage — the Backtest Lab (plan §15 §5). A form (symbol/timeframe from the datasets, style,
// date range bounded by the dataset, starting balance, risk %) runs POST /api/backtest via the
// useRunBacktest mutation. Results: KPI tiles + the Recharts balance/ΣR curve + the reusable
// TradesTable of the run's trades. Recent runs are kept in state so the operator can compare. Inline
// 400/404 errors surface the host's {error} message.
//
// The Optimizer deep-links here with ?symbol=&timeframe=&style=&risk= to pre-fill a combination. The
// trades shown are SIMULATED/advisory — there is no live counterpart anywhere (plan §6.3).
// ---------------------------------------------------------------------------------------------------

import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useBacktestDatasets, useRunBacktest } from '../api/hooks';
import type { BacktestRequest, BacktestResponse } from '../types/api';
import { UNDEFINED_PROFIT_FACTOR } from '../types/api';
import { STYLES } from '../components/ChartPanel';
import { MODELS, DEFAULT_MODEL, modelLabel } from '../models';
import { KpiTiles, type KpiTile } from '../components/KpiTiles';
import { BalanceCurve } from '../components/BalanceCurve';
import { TradesTable } from '../components/TradesTable';
import { deltaPercent, formatMoney, formatPct } from '../format';
import { errorMessage } from '../format-error';

/** A UTC ISO instant → the yyyy-MM-dd a <input type="date"> expects. */
function toDateInput(utcIso: string): string {
  return utcIso.slice(0, 10);
}

/** A yyyy-MM-dd date-input value → a UTC ISO midnight instant. */
function fromDateInput(date: string): string {
  return `${date}T00:00:00Z`;
}

function summaryTiles(run: BacktestResponse): KpiTile[] {
  const delta = deltaPercent(run.endingBalance, run.startingBalance);
  const pf =
    run.summary.tradeCount === 0
      ? 'n/a'
      : run.summary.profitFactor >= UNDEFINED_PROFIT_FACTOR
        ? '∞'
        : run.summary.profitFactor.toFixed(2);
  return [
    {
      label: 'Ending balance',
      value: `${formatMoney(run.endingBalance)} (${delta >= 0 ? '+' : ''}${delta.toFixed(2)}%)`,
      tone: delta >= 0 ? 'long' : 'short',
    },
    { label: 'Trades', value: `${run.tradeCount}` },
    { label: 'Setups', value: `${run.setupCount}` },
    { label: 'Win rate', value: formatPct(run.summary.winRate), tone: 'long' },
    {
      label: 'Avg R',
      value: `${run.summary.averageR.toFixed(2)}R`,
      tone: run.summary.averageR >= 0 ? 'long' : 'short',
    },
    { label: 'Profit factor', value: pf },
    { label: 'Max DD (R)', value: `${run.summary.maxDrawdown.toFixed(2)}R`, tone: 'short' },
  ];
}

export function BacktestPage(): React.JSX.Element {
  const datasetsQ = useBacktestDatasets();
  const datasets = useMemo(() => datasetsQ.data ?? [], [datasetsQ.data]);
  const runBacktest = useRunBacktest();

  const [searchParams] = useSearchParams();

  // Form values use a render-time-derived default: a field is `null` until the operator overrides it,
  // and falls back to the URL deep-link (Optimizer drill-in) or the first dataset. This avoids an
  // effect that would otherwise have to setState once datasets load asynchronously.
  const [symbolOverride, setSymbolOverride] = useState<string | null>(null);
  const [timeframeOverride, setTimeframeOverride] = useState<string | null>(null);
  const [style, setStyle] = useState<string>(searchParams.get('style') ?? 'Intraday');
  const [startingBalance, setStartingBalance] = useState(10000);
  const [riskPercent, setRiskPercent] = useState(
    searchParams.get('risk') ? Number(searchParams.get('risk')) : 1,
  );
  const [fromOverride, setFromOverride] = useState<string | null>(null);
  const [toOverride, setToOverride] = useState<string | null>(null);
  const [minRequired, setMinRequired] = useState('');
  const [model, setModel] = useState<string>(searchParams.get('model') ?? DEFAULT_MODEL);

  const [runs, setRuns] = useState<BacktestResponse[]>([]);

  // The default symbol: explicit override → URL deep-link → first dataset.
  const urlSymbol = searchParams.get('symbol');
  const defaultSymbol =
    symbolOverride ??
    (datasets.find((d) => d.symbol === urlSymbol)?.symbol ?? datasets[0]?.symbol ?? '');
  const symbol = defaultSymbol;

  // The datasets for the selected symbol (drives the timeframe + the date-range bounds).
  const symbolDatasets = useMemo(
    () => datasets.filter((d) => d.symbol === symbol),
    [datasets, symbol],
  );

  const urlTf = searchParams.get('timeframe');
  const timeframe =
    timeframeOverride ??
    (symbolDatasets.find((d) => d.timeframe === urlTf)?.timeframe ?? symbolDatasets[0]?.timeframe ?? '');

  const activeDataset = useMemo(
    () => symbolDatasets.find((d) => d.timeframe === timeframe) ?? symbolDatasets[0],
    [symbolDatasets, timeframe],
  );

  const fromDate = fromOverride ?? (activeDataset ? toDateInput(activeDataset.fromUtc) : '');
  const toDate = toOverride ?? (activeDataset ? toDateInput(activeDataset.toUtc) : '');

  // Switching the symbol resets the timeframe + date overrides so they re-derive from the new dataset.
  function onSymbolChange(next: string): void {
    setSymbolOverride(next);
    setTimeframeOverride(null);
    setFromOverride(null);
    setToOverride(null);
  }

  function onSubmit(e: React.FormEvent): void {
    e.preventDefault();
    if (!symbol) return;
    const req: BacktestRequest = {
      symbol,
      style,
      startingBalance,
      riskPercent,
      ...(timeframe ? { timeframe } : {}),
      ...(fromDate ? { fromUtc: fromDateInput(fromDate) } : {}),
      ...(toDate ? { toUtc: fromDateInput(toDate) } : {}),
      ...(minRequired ? { minRequiredConditions: Number(minRequired) } : {}),
      ...(model ? { model } : {}),
    };
    runBacktest.mutate(req, {
      onSuccess: (run) => setRuns((prev) => [run, ...prev].slice(0, 5)),
    });
  }

  const latest = runs[0];

  return (
    <div className="page page--backtest">
      <section className="panel" aria-label="Backtest configuration">
        <header className="panel__head">
          <span>Backtest Lab</span>
          <span className="badge-advisory">Paper</span>
        </header>
        <div className="panel__body">
          <form className="form" onSubmit={onSubmit} aria-label="Backtest form">
            <label className="form__field">
              <span>Symbol</span>
              <select
                className="input"
                value={symbol}
                aria-label="Backtest symbol"
                onChange={(e) => onSymbolChange(e.target.value)}
              >
                {[...new Set(datasets.map((d) => d.symbol))].map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </label>

            <label className="form__field">
              <span>Timeframe</span>
              <select
                className="input"
                value={timeframe}
                aria-label="Backtest timeframe"
                onChange={(e) => setTimeframeOverride(e.target.value)}
              >
                {symbolDatasets.map((d) => (
                  <option key={d.timeframe} value={d.timeframe}>
                    {d.timeframe}
                  </option>
                ))}
              </select>
            </label>

            <label className="form__field">
              <span>Style</span>
              <select
                className="input"
                value={style}
                aria-label="Backtest style"
                onChange={(e) => setStyle(e.target.value)}
              >
                {STYLES.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </label>

            <label className="form__field">
              <span>Model</span>
              <select
                className="input"
                value={model}
                aria-label="Backtest model"
                onChange={(e) => setModel(e.target.value)}
              >
                {MODELS.map((m) => (
                  <option key={m.value} value={m.value}>
                    {m.label}
                  </option>
                ))}
              </select>
            </label>

            <label className="form__field">
              <span>From</span>
              <input
                type="date"
                className="input"
                aria-label="From date"
                value={fromDate}
                min={activeDataset ? toDateInput(activeDataset.fromUtc) : undefined}
                max={toDate || (activeDataset ? toDateInput(activeDataset.toUtc) : undefined)}
                onChange={(e) => setFromOverride(e.target.value)}
              />
            </label>

            <label className="form__field">
              <span>To</span>
              <input
                type="date"
                className="input"
                aria-label="To date"
                value={toDate}
                min={fromDate || (activeDataset ? toDateInput(activeDataset.fromUtc) : undefined)}
                max={activeDataset ? toDateInput(activeDataset.toUtc) : undefined}
                onChange={(e) => setToOverride(e.target.value)}
              />
            </label>

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
              <span>Risk %</span>
              <input
                type="number"
                className="input"
                aria-label="Risk percent"
                min={0.05}
                max={4.5}
                step="any"
                value={riskPercent}
                onChange={(e) => setRiskPercent(Number(e.target.value))}
              />
            </label>

            <label className="form__field">
              <span>Min required (k of n)</span>
              <input
                type="number"
                className="input"
                aria-label="Min required conditions"
                min={1}
                max={8}
                step={1}
                placeholder="all"
                value={minRequired}
                onChange={(e) => setMinRequired(e.target.value)}
              />
            </label>

            <div className="form__actions">
              <button
                type="submit"
                className="btn btn--primary"
                disabled={runBacktest.isPending || !symbol}
              >
                {runBacktest.isPending ? 'Running…' : 'Run Backtest'}
              </button>
              {activeDataset ? (
                <span className="form__hint num">
                  {activeDataset.candleCount.toLocaleString()} candles ·{' '}
                  {toDateInput(activeDataset.fromUtc)} → {toDateInput(activeDataset.toUtc)}
                </span>
              ) : null}
            </div>
          </form>

          {runBacktest.isError ? (
            <p className="empty error" role="alert">
              Backtest failed — {errorMessage(runBacktest.error)}
            </p>
          ) : null}
        </div>
      </section>

      {latest ? (
        <>
          <section className="panel" aria-label="Backtest results">
            <header className="panel__head">
              <span>
                Results — {latest.symbol} {latest.timeframe} {latest.style} ·{' '}
                {modelLabel(latest.model)} · {riskPercent}% risk
              </span>
              <span className="num neutral">{latest.candlesProcessed.toLocaleString()} candles</span>
            </header>
            <div className="panel__body">
              <KpiTiles tiles={summaryTiles(latest)} />
              <BalanceCurve equity={latest.equity} />
            </div>
          </section>

          <section className="panel" aria-label="Backtest trades">
            <header className="panel__head">
              <span>Trades ({latest.trades.length})</span>
            </header>
            <div className="panel__body panel__body--flush">
              <TradesTable trades={latest.trades} emptyMessage="No trades in this run." />
            </div>
          </section>

          {runs.length > 1 ? (
            <section className="panel" aria-label="Recent runs">
              <header className="panel__head">
                <span>Recent Runs</span>
              </header>
              <div className="panel__body panel__body--flush">
                <table className="tbl" aria-label="Recent runs">
                  <thead>
                    <tr>
                      <th>#</th>
                      <th>Symbol</th>
                      <th>TF</th>
                      <th>Style</th>
                      <th>Risk %</th>
                      <th>Trades</th>
                      <th>Win %</th>
                      <th>Avg R</th>
                      <th>Ending</th>
                    </tr>
                  </thead>
                  <tbody>
                    {runs.map((r, i) => (
                      <tr key={`${r.symbol}-${r.timeframe}-${r.style}-${i}`}>
                        <td className="num neutral">{runs.length - i}</td>
                        <td className="num">{r.symbol}</td>
                        <td className="num">{r.timeframe}</td>
                        <td>{r.style}</td>
                        <td className="num">{r.riskPercent}</td>
                        <td className="num">{r.tradeCount}</td>
                        <td className="num">{formatPct(r.summary.winRate)}</td>
                        <td className={`num ${r.summary.averageR >= 0 ? 'long' : 'short'}`}>
                          {r.summary.averageR.toFixed(2)}R
                        </td>
                        <td
                          className={`num ${r.endingBalance >= r.startingBalance ? 'long' : 'short'}`}
                        >
                          {formatMoney(r.endingBalance)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          ) : null}
        </>
      ) : (
        <section className="panel">
          <div className="panel__body">
            <p className="empty">Configure a run and press “Run Backtest” to see results.</p>
          </div>
        </section>
      )}
    </div>
  );
}
