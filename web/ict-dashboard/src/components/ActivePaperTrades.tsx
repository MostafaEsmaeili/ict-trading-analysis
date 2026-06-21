// ---------------------------------------------------------------------------------------------------
// Active Paper Trades (right panel, plan §9) — entry/stop/target/status/time-in-trade/live R for each
// SIMULATED trade. There is no live counterpart anywhere in the system (plan §6.3); every row is
// advisory paper. Live R is null until the trade closes (the fill simulator books it — WP5).
// ---------------------------------------------------------------------------------------------------

import { formatDistanceToNowStrict } from 'date-fns';
import type { PaperTradeDto } from '../types/api';
import { directionTone } from '../theme';
import { DirectionChip, KillzoneBadge, StyleChip } from './Badges';

export interface ActivePaperTradesProps {
  trades: PaperTradeDto[];
  isLoading: boolean;
  onFocusSymbol?: (symbol: string) => void;
}

function fmtPrice(p: number): string {
  return p.toFixed(5);
}

function timeInTrade(openedAtUtc: string): string {
  return formatDistanceToNowStrict(new Date(openedAtUtc));
}

export function ActivePaperTrades({
  trades,
  isLoading,
  onFocusSymbol,
}: ActivePaperTradesProps): React.JSX.Element {
  return (
    <section className="panel" aria-label="Active paper trades">
      <header className="panel__head">
        <span>Active Paper Trades</span>
        <span className="badge-advisory">Paper</span>
      </header>
      <div className="panel__body panel__body--flush">
        {isLoading ? (
          <p className="empty">Loading trades…</p>
        ) : trades.length === 0 ? (
          <p className="empty">No open paper trades.</p>
        ) : (
          <table className="tbl">
            <thead>
              <tr>
                <th>Symbol</th>
                <th>Entry</th>
                <th>Stop</th>
                <th>Target</th>
                <th>In trade</th>
                <th>R</th>
              </tr>
            </thead>
            <tbody>
              {trades.map((t) => {
                const tone = directionTone(t.direction);
                const target = t.targets.at(-1);
                return (
                  <tr key={t.id} onClick={() => onFocusSymbol?.(t.symbol)} style={{ cursor: 'pointer' }}>
                    <td>
                      <div style={{ display: 'flex', gap: 4, alignItems: 'center', flexWrap: 'wrap' }}>
                        <span className="num" style={{ fontWeight: 700 }}>
                          {t.symbol}
                        </span>
                        <DirectionChip direction={t.direction} />
                        <StyleChip style={t.style} />
                        <KillzoneBadge killzone={t.killzone} />
                      </div>
                    </td>
                    <td className="num">{fmtPrice(t.entry)}</td>
                    <td className="num short">{fmtPrice(t.stop)}</td>
                    <td className="num long">{target !== undefined ? fmtPrice(target) : '—'}</td>
                    <td className="num neutral">{timeInTrade(t.openedAtUtc)}</td>
                    <td className={`num ${t.realizedR == null ? 'neutral' : tone}`}>
                      {t.realizedR == null ? 'open' : `${t.realizedR.toFixed(2)}R`}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </section>
  );
}
