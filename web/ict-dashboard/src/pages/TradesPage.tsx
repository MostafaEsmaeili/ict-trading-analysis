// ---------------------------------------------------------------------------------------------------
// TradesPage (plan §15 §4) — the full paper-trade history. A filter bar (status / symbol / style /
// win-loss) drives the server-side status+symbol filter (useAllTrades) plus the client-side style +
// win/loss filter; the reusable TradesTable renders the sortable grid with a totals row. Row click
// deep-links to the Live chart focused on that symbol/time.
//
// Read-only: every row is an ADVISORY paper trade — no execute/close/modify control (plan §6.3).
// ---------------------------------------------------------------------------------------------------

import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAllTrades } from '../api/hooks';
import type { TradeFilters } from '../api/client';
import type { PaperTradeDto } from '../types/api';
import { STYLES, SYMBOLS } from '../components/ChartPanel';
import { MODELS } from '../models';
import { TradesTable, type TradeFocusTarget } from '../components/TradesTable';

type StatusFilter = 'All' | 'Open' | 'Closed';
type StyleFilter = 'All' | (typeof STYLES)[number];
type WinLossFilter = 'All' | 'Win' | 'Loss';
type ModelFilter = 'All' | (typeof MODELS)[number]['value'];

export function TradesPage(): React.JSX.Element {
  const navigate = useNavigate();
  const [status, setStatus] = useState<StatusFilter>('All');
  const [symbol, setSymbol] = useState<string>('All');
  const [style, setStyle] = useState<StyleFilter>('All');
  const [winLoss, setWinLoss] = useState<WinLossFilter>('All');
  const [model, setModel] = useState<ModelFilter>('All');

  // Status + symbol are server-side filters (the wire supports them); style + win/loss are client-side.
  const serverFilters: TradeFilters = useMemo(
    () => ({
      ...(status !== 'All' ? { status } : {}),
      ...(symbol !== 'All' ? { symbol } : {}),
    }),
    [status, symbol],
  );

  const tradesQ = useAllTrades(serverFilters);

  const filtered = useMemo(() => {
    const all = tradesQ.data ?? [];
    return all.filter((t: PaperTradeDto) => {
      if (style !== 'All' && t.style !== style) return false;
      if (model !== 'All' && t.model !== model) return false;
      if (winLoss === 'Win' && (t.realizedR ?? 0) <= 0) return false;
      if (winLoss === 'Loss' && (t.realizedR ?? 0) >= 0) return false;
      return true;
    });
  }, [tradesQ.data, style, winLoss, model]);

  const handleFocus = (target: TradeFocusTarget): void => {
    // Deep-link to the Live page; the symbol is carried in the URL so the Live chart can pick it up.
    navigate(`/?symbol=${encodeURIComponent(target.symbol)}`);
  };

  return (
    <div className="page page--trades">
      <section className="panel" aria-label="Trade history">
        <header className="panel__head">
          <span>Trade History</span>
          <span className="badge-advisory">Paper</span>
        </header>

        <div className="filterbar" role="group" aria-label="Trade filters">
          <label className="filterbar__field">
            <span>Status</span>
            <select
              className="input"
              value={status}
              aria-label="Status filter"
              onChange={(e) => setStatus(e.target.value as StatusFilter)}
            >
              {(['All', 'Open', 'Closed'] as const).map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>

          <label className="filterbar__field">
            <span>Symbol</span>
            <select
              className="input"
              value={symbol}
              aria-label="Symbol filter"
              onChange={(e) => setSymbol(e.target.value)}
            >
              <option value="All">All</option>
              {SYMBOLS.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>

          <label className="filterbar__field">
            <span>Style</span>
            <select
              className="input"
              value={style}
              aria-label="Style filter"
              onChange={(e) => setStyle(e.target.value as StyleFilter)}
            >
              <option value="All">All</option>
              {STYLES.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>

          <label className="filterbar__field">
            <span>Model</span>
            <select
              className="input"
              value={model}
              aria-label="Model filter"
              onChange={(e) => setModel(e.target.value as ModelFilter)}
            >
              <option value="All">All</option>
              {MODELS.map((m) => (
                <option key={m.value} value={m.value}>
                  {m.label}
                </option>
              ))}
            </select>
          </label>

          <label className="filterbar__field">
            <span>Result</span>
            <select
              className="input"
              value={winLoss}
              aria-label="Win/loss filter"
              onChange={(e) => setWinLoss(e.target.value as WinLossFilter)}
            >
              {(['All', 'Win', 'Loss'] as const).map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
        </div>

        <div className="panel__body panel__body--flush">
          <TradesTable
            trades={filtered}
            isLoading={tradesQ.isLoading}
            isError={tradesQ.isError}
            error={tradesQ.error}
            emptyMessage="No trades match the filters."
            onFocus={handleFocus}
          />
        </div>
      </section>
    </div>
  );
}
