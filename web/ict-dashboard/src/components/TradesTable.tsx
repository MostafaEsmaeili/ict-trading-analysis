// ---------------------------------------------------------------------------------------------------
// TradesTable (plan §15 §4) — the reusable, sortable trades grid the Trades-history page AND the
// Backtest-results panel both render. Columns: symbol + chips, direction, style, status, entry, stop,
// current stop, targets, size, opened/closed (NY time), close-reason, net P&L (money, colored), R
// (netR; tooltip realizedR). A footer totals row shows count, net-P&L sum and win rate.
//
// Read-only: every row is an ADVISORY paper trade — there is NO execute/close/modify control here
// (plan §6.3). Row click is an optional focus callback (deep-link to the live chart).
// ---------------------------------------------------------------------------------------------------

import { useMemo, useState } from 'react';
import type {
  Killzone,
  PaperTradeDto,
  TradeCloseReason,
  TradeDirection,
  TradeStatus,
  TradeStyle,
} from '../types/api';
import { directionTone } from '../theme';
import { formatMoney, formatPrice, formatR } from '../format';
import { formatNyDateTime } from '../time';
import {
  CloseReasonPill,
  DirectionChip,
  KillzoneBadge,
  ModelBadge,
  StatusPill,
  StyleChip,
} from './Badges';

/** A focus target for the optional row deep-link (mirrors AlertsFeed.FocusTarget). */
export interface TradeFocusTarget {
  symbol: string;
  atUtc?: string;
}

export interface TradesTableProps {
  trades: PaperTradeDto[];
  isLoading?: boolean;
  isError?: boolean;
  error?: unknown;
  emptyMessage?: string;
  /** Optional click handler — deep-link the row to the live chart focused on its symbol/time. */
  onFocus?: (target: TradeFocusTarget) => void;
}

type SortKey = 'symbol' | 'opened' | 'closed' | 'netPnl' | 'netR' | 'status';
type SortDir = 'asc' | 'desc';

function isWin(t: PaperTradeDto): boolean {
  return (t.realizedR ?? 0) > 0;
}

function sortValue(t: PaperTradeDto, key: SortKey): number | string {
  switch (key) {
    case 'symbol':
      return t.symbol;
    case 'opened':
      return Date.parse(t.openedAtUtc);
    case 'closed':
      return t.closedAtUtc ? Date.parse(t.closedAtUtc) : 0;
    case 'netPnl':
      return t.netPnl ?? Number.NEGATIVE_INFINITY;
    case 'netR':
      return t.netR ?? t.realizedR ?? Number.NEGATIVE_INFINITY;
    case 'status':
      return t.status;
  }
}

export function TradesTable({
  trades,
  isLoading,
  isError,
  error,
  emptyMessage = 'No trades.',
  onFocus,
}: TradesTableProps): React.JSX.Element {
  const [sortKey, setSortKey] = useState<SortKey>('opened');
  const [sortDir, setSortDir] = useState<SortDir>('desc');

  const sorted = useMemo(() => {
    const copy = [...trades];
    copy.sort((a, b) => {
      const av = sortValue(a, sortKey);
      const bv = sortValue(b, sortKey);
      const cmp = typeof av === 'string' ? av.localeCompare(bv as string) : av - (bv as number);
      return sortDir === 'asc' ? cmp : -cmp;
    });
    return copy;
  }, [trades, sortKey, sortDir]);

  const totals = useMemo(() => {
    const closed = trades.filter((t) => t.status === 'Closed');
    const wins = closed.filter(isWin).length;
    const netPnlSum = closed.reduce((acc, t) => acc + (t.netPnl ?? 0), 0);
    const winRate = closed.length > 0 ? wins / closed.length : 0;
    return { count: trades.length, closedCount: closed.length, netPnlSum, winRate };
  }, [trades]);

  function toggleSort(key: SortKey): void {
    if (key === sortKey) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortKey(key);
      setSortDir(key === 'symbol' ? 'asc' : 'desc');
    }
  }

  function sortIndicator(key: SortKey): string {
    if (key !== sortKey) return '';
    return sortDir === 'asc' ? ' ▲' : ' ▼';
  }

  if (isError) {
    return (
      <p className="empty error" role="alert">
        Trades unavailable — {error instanceof Error ? error.message : 'host error'}
      </p>
    );
  }
  if (isLoading) {
    return <p className="empty">Loading trades…</p>;
  }
  if (trades.length === 0) {
    return <p className="empty">{emptyMessage}</p>;
  }

  const sortableHeader = (key: SortKey, label: string) => (
    <th>
      <button
        type="button"
        className="th-sort"
        aria-label={`Sort by ${label}`}
        onClick={() => toggleSort(key)}
      >
        {label}
        {sortIndicator(key)}
      </button>
    </th>
  );

  return (
    <table className="tbl tbl--trades" aria-label="Trades">
      <thead>
        <tr>
          {sortableHeader('symbol', 'Symbol')}
          <th>Dir</th>
          <th>Style</th>
          <th>Model</th>
          {sortableHeader('status', 'Status')}
          <th>Entry</th>
          <th>Stop</th>
          <th>Cur stop</th>
          <th>Target</th>
          <th>Size</th>
          {sortableHeader('opened', 'Opened (NY)')}
          {sortableHeader('closed', 'Closed (NY)')}
          <th>Close</th>
          {sortableHeader('netPnl', 'Net P&L')}
          {sortableHeader('netR', 'R')}
        </tr>
      </thead>
      <tbody>
        {sorted.map((t) => {
          const tone = directionTone(t.direction);
          const target = t.targets.at(-1);
          const focus = onFocus
            ? () => onFocus({ symbol: t.symbol, atUtc: t.openedAtUtc })
            : undefined;
          const rShown = t.netR ?? t.realizedR;
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
                <span className="num" style={{ fontWeight: 700 }}>
                  {t.symbol}
                </span>
              </td>
              <td>
                <DirectionChip direction={t.direction as TradeDirection} />
              </td>
              <td>
                <StyleChip style={t.style as TradeStyle} />
              </td>
              <td>
                <ModelBadge model={t.model} />
              </td>
              <td>
                <div style={{ display: 'flex', gap: 4, alignItems: 'center', justifyContent: 'flex-end' }}>
                  <StatusPill status={t.status as TradeStatus} />
                  <KillzoneBadge killzone={t.killzone as Killzone | null} />
                </div>
              </td>
              <td className="num">{formatPrice(t.entry, t.symbol)}</td>
              <td className="num short">{formatPrice(t.stop, t.symbol)}</td>
              <td className="num neutral">{formatPrice(t.currentStop, t.symbol)}</td>
              <td className="num long">
                {target !== undefined ? formatPrice(target, t.symbol) : '—'}
              </td>
              <td className="num neutral">{t.size.toFixed(2)}</td>
              <td className="num neutral">{formatNyDateTime(t.openedAtUtc)}</td>
              <td className="num neutral">
                {t.closedAtUtc ? formatNyDateTime(t.closedAtUtc) : '—'}
              </td>
              <td>
                <CloseReasonPill reason={t.closeReason as TradeCloseReason | null} />
              </td>
              <td className={`num ${t.netPnl == null ? 'neutral' : t.netPnl >= 0 ? 'long' : 'short'}`}>
                {formatMoney(t.netPnl, { signed: true })}
              </td>
              <td
                className={`num ${rShown == null ? 'neutral' : rShown >= 0 ? tone : 'short'}`}
                title={t.realizedR == null ? undefined : `realized ${t.realizedR.toFixed(2)}R`}
              >
                {rShown == null ? 'open' : formatR(rShown)}
              </td>
            </tr>
          );
        })}
      </tbody>
      <tfoot>
        <tr className="tbl__totals">
          <td colSpan={10}>
            {totals.count} trades · {totals.closedCount} closed · win{' '}
            {(totals.winRate * 100).toFixed(0)}%
          </td>
          <td colSpan={3} />
          <td className={`num ${totals.netPnlSum >= 0 ? 'long' : 'short'}`}>
            {formatMoney(totals.netPnlSum, { signed: true })}
          </td>
          <td />
        </tr>
      </tfoot>
    </table>
  );
}
