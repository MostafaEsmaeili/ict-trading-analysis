// ---------------------------------------------------------------------------------------------------
// Active Paper Trades (right panel, plan §9) — entry/stop/target/status/time-in-trade/live R for each
// SIMULATED trade. There is no live counterpart anywhere in the system (plan §6.3); every row is
// advisory paper. Live R is null until the trade closes (the fill simulator books it — WP5).
// ---------------------------------------------------------------------------------------------------

import { formatDistanceToNowStrict } from 'date-fns';
import type { Killzone, PaperTradeDto, TradeDirection, TradeStyle } from '../types/api';
import { directionTone } from '../theme';
import { formatPrice } from '../format';
import { errorMessage } from '../format-error';
import { DirectionChip, KillzoneBadge, StyleChip } from './Badges';
import type { FocusTarget } from './AlertsFeed';

export interface ActivePaperTradesProps {
  trades: PaperTradeDto[];
  isLoading: boolean;
  isError?: boolean;
  error?: unknown;
  onFocus?: (target: FocusTarget) => void;
}

function timeInTrade(openedAtUtc: string): string {
  return formatDistanceToNowStrict(new Date(openedAtUtc));
}

export function ActivePaperTrades({
  trades,
  isLoading,
  isError,
  error,
  onFocus,
}: ActivePaperTradesProps): React.JSX.Element {
  return (
    <section className="panel" aria-label="Active paper trades">
      <header className="panel__head">
        <span>Active Paper Trades</span>
        <span className="badge-advisory">Paper</span>
      </header>
      <div className="panel__body panel__body--flush">
        {isError ? (
          <p className="empty error" role="alert">
            Trades unavailable — {errorMessage(error)}
          </p>
        ) : isLoading ? (
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
                const focus = onFocus
                  ? () => onFocus({ symbol: t.symbol, atUtc: t.openedAtUtc })
                  : undefined;
                return (
                  <tr
                    key={t.id}
                    {...(focus
                      ? {
                          role: 'button',
                          tabIndex: 0,
                          'aria-label': `Focus chart on ${t.symbol}`,
                          onClick: focus,
                          onKeyDown: (e: React.KeyboardEvent<HTMLTableRowElement>) => {
                            if (e.key === 'Enter' || e.key === ' ') {
                              e.preventDefault();
                              focus();
                            }
                          },
                          style: { cursor: 'pointer' },
                        }
                      : {})}
                  >
                    <td>
                      <div style={{ display: 'flex', gap: 4, alignItems: 'center', flexWrap: 'wrap' }}>
                        <span className="num" style={{ fontWeight: 700 }}>
                          {t.symbol}
                        </span>
                        <DirectionChip direction={t.direction as TradeDirection} />
                        <StyleChip style={t.style as TradeStyle} />
                        <KillzoneBadge killzone={t.killzone as Killzone | null} />
                      </div>
                    </td>
                    <td className="num">{formatPrice(t.entry, t.symbol)}</td>
                    <td className="num short">{formatPrice(t.stop, t.symbol)}</td>
                    <td className="num long">
                      {target !== undefined ? formatPrice(target, t.symbol) : '—'}
                    </td>
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
