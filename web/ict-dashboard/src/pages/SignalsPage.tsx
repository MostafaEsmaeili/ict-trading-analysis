// ---------------------------------------------------------------------------------------------------
// SignalsPage — the ranked SIGNALS view + manual TAKE workflow (plan §15, approved frontend design §1–4).
// A filterable list (symbol / style / grade / timeframe / min-RR / hide-taken) over the useSignals cache,
// rendered by SignalsFeed; the Take button opens a PAPER trade via useTakeSignal and focuses the chart on
// a row click (it deep-links to Live with the signal's symbol so the chart switches there).
//
// React Query owns the data; SignalsUpdated pushes the full ranked list onto the SAME cache (useTradingHub
// on the Live page). Read-only/advisory: Take opens a PAPER trade only — no live order path (§6.3).
// ---------------------------------------------------------------------------------------------------

import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useSignals, useTakeSignal } from '../api/hooks';
import { SignalsFeed } from '../components/SignalsFeed';
import { SYMBOLS, TIMEFRAMES, STYLES } from '../components/ChartPanel';
import { errorMessage } from '../format-error';
import type { FocusTarget } from '../components/AlertsFeed';

const GRADES = ['A', 'B', 'C'] as const;

export function SignalsPage(): React.JSX.Element {
  const signalsQ = useSignals();
  const take = useTakeSignal();
  const navigate = useNavigate();

  const [symbol, setSymbol] = useState('');
  const [style, setStyle] = useState('');
  const [grade, setGrade] = useState('');
  const [timeframe, setTimeframe] = useState('');
  const [minRr, setMinRr] = useState('');
  const [hideTaken, setHideTaken] = useState(false);

  const all = useMemo(() => signalsQ.data ?? [], [signalsQ.data]);

  // Client-side filtering over the one cache (mirrors how the Trades/Optimizer pages narrow their data).
  const filtered = useMemo(() => {
    const minRrNum = minRr.trim() === '' ? null : Number(minRr);
    return all.filter((s) => {
      if (symbol && s.setup.symbol !== symbol) return false;
      if (style && s.setup.style !== style) return false;
      if (grade && s.setup.grade !== grade) return false;
      if (timeframe && s.setup.triggerTimeframe !== timeframe) return false;
      if (minRrNum != null && Number.isFinite(minRrNum) && s.setup.rewardRatio < minRrNum) return false;
      if (hideTaken && (s.isTaken || s.blockReason)) return false;
      return true;
    });
  }, [all, symbol, style, grade, timeframe, minRr, hideTaken]);

  // A row focus deep-links to Live with the signal's symbol so the chart there switches to it (the Live
  // page reads the `?symbol=` seed). The signal's TF/style ride along too where the Live chart honours them.
  const handleFocus = (target: FocusTarget): void => {
    navigate(`/?symbol=${encodeURIComponent(target.symbol)}`);
  };

  // The take-mutation's last error (a 404/409 surfaces here) — rendered above the feed so the operator
  // sees WHY a take was refused (a defensive failure must look different from a healthy one, §6.3).
  const takeError = take.isError ? errorMessage(take.error) : '';
  const takingId = take.isPending ? take.variables?.setupId ?? null : null;

  return (
    <div className="page page--signals">
      <section className="panel" aria-label="Signal filters">
        <header className="panel__head">
          <span>Filter signals</span>
          <span className="num neutral">
            {filtered.length} of {all.length}
          </span>
        </header>
        <div className="filterbar">
          <label className="filterbar__field">
            <span>Symbol</span>
            <select className="input" aria-label="Symbol filter" value={symbol} onChange={(e) => setSymbol(e.target.value)}>
              <option value="">All</option>
              {SYMBOLS.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
          <label className="filterbar__field">
            <span>Style</span>
            <select className="input" aria-label="Style filter" value={style} onChange={(e) => setStyle(e.target.value)}>
              <option value="">All</option>
              {STYLES.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
          <label className="filterbar__field">
            <span>Grade</span>
            <select className="input" aria-label="Grade filter" value={grade} onChange={(e) => setGrade(e.target.value)}>
              <option value="">All</option>
              {GRADES.map((g) => (
                <option key={g} value={g}>
                  {g}
                </option>
              ))}
            </select>
          </label>
          <label className="filterbar__field">
            <span>Timeframe</span>
            <select
              className="input"
              aria-label="Timeframe filter"
              value={timeframe}
              onChange={(e) => setTimeframe(e.target.value)}
            >
              <option value="">All</option>
              {TIMEFRAMES.map((tf) => (
                <option key={tf} value={tf}>
                  {tf}
                </option>
              ))}
            </select>
          </label>
          <label className="filterbar__field">
            <span>Min RR</span>
            <input
              className="input"
              type="number"
              min={0}
              step={0.1}
              aria-label="Minimum reward ratio"
              value={minRr}
              onChange={(e) => setMinRr(e.target.value)}
              placeholder="any"
              style={{ width: 90 }}
            />
          </label>
          <label className="filterbar__field filterbar__field--check">
            <span>Hide taken</span>
            <input
              type="checkbox"
              aria-label="Hide taken signals"
              checked={hideTaken}
              onChange={(e) => setHideTaken(e.target.checked)}
            />
          </label>
        </div>
        {takeError ? (
          <p className="empty error" role="alert">
            Take failed — {takeError}
          </p>
        ) : null}
      </section>

      <SignalsFeed
        signals={filtered}
        isLoading={signalsQ.isLoading}
        isError={signalsQ.isError}
        error={signalsQ.error}
        onTake={(setupId) => take.mutate({ setupId })}
        takingId={takingId}
        onFocus={handleFocus}
      />
    </div>
  );
}
