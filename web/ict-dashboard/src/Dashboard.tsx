// ---------------------------------------------------------------------------------------------------
// Dashboard — the three-panel ICT trading-desk shell (plan §9): center ICT Pattern Chart, left Alerts
// Feed, right/bottom Active Paper Trades + Performance. This component is PRESENTATIONAL: all
// orchestration lives in custom hooks (useMarketSelection / useOverlayVisibility / useNyClock /
// useDashboardData per the repo guideline). React Query owns server state; SignalR deltas merge into
// the same keys via useTradingHub (live in a non-mocks build; mocks mode reconciles on the poll).
//
// DEFENSIVE: read-only analysis only. There is NO execute/go-live/order control anywhere — the header
// carries an "Advisory / Paper" posture badge instead (plan §6.3).
// ---------------------------------------------------------------------------------------------------

import { useCallback, useState } from 'react';
import { AlertsFeed, type FocusTarget } from './components/AlertsFeed';
import { ActivePaperTrades } from './components/ActivePaperTrades';
import { ChartPanel } from './components/ChartPanel';
import { PerformancePanel } from './components/PerformancePanel';
import { useMarketSelection } from './hooks/useMarketSelection';
import { useOverlayVisibility } from './hooks/useOverlayVisibility';
import { useNyClock } from './hooks/useNyClock';
import { useDashboardData } from './hooks/useDashboardData';

export function Dashboard(): React.JSX.Element {
  const { symbol, timeframe, style, setSymbol, setTimeframe, selectStyle } = useMarketSelection();
  const { visibility, toggleOverlay } = useOverlayVisibility();
  const clock = useNyClock();

  // Focus-on-alert/trade: switch the symbol AND seek the chart to the clicked moment. The clicked DTOs
  // (AlertDto/PaperTradeDto) carry no timeframe, so the operator's selected TF is kept; only the symbol
  // + the seek instant change (a contract change would be needed to also switch to the setup's TF).
  const [seekToUtc, setSeekToUtc] = useState<string | undefined>(undefined);
  const handleFocus = useCallback(
    (target: FocusTarget) => {
      setSymbol(target.symbol);
      setSeekToUtc(target.atUtc);
    },
    [setSymbol],
  );

  const { candlesQ, overlaysQ, alertsQ, tradesQ, perfQ, equityQ, activeKillzone, triggerTimeframe } =
    useDashboardData({ symbol, timeframe, style });

  return (
    <div className="app">
      <header className="app__header">
        <div className="app__brand">
          ICT Trading Desk
          <small>analysis &amp; paper-trading · read-only</small>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <span className="badge-advisory">Advisory · Paper only</span>
          <span className="app__clock">{clock}</span>
        </div>
      </header>

      <div className="layout">
        <AlertsFeed
          alerts={alertsQ.data ?? []}
          isLoading={alertsQ.isLoading}
          isError={alertsQ.isError}
          error={alertsQ.error}
          onFocus={handleFocus}
        />

        <ChartPanel
          symbol={symbol}
          timeframe={timeframe}
          style={style}
          candles={candlesQ.data ?? []}
          overlays={overlaysQ.data ?? []}
          visibility={visibility}
          isLoading={candlesQ.isLoading}
          // The chart surfaces an error if EITHER its candles or its overlays query fails.
          isError={candlesQ.isError || overlaysQ.isError}
          error={candlesQ.error ?? overlaysQ.error}
          seekToUtc={seekToUtc}
          activeKillzone={activeKillzone}
          triggerTimeframe={triggerTimeframe}
          onSymbolChange={setSymbol}
          onTimeframeChange={setTimeframe}
          onStyleChange={selectStyle}
          onToggleOverlay={toggleOverlay}
        />

        <div className="layout__trades">
          <ActivePaperTrades
            trades={tradesQ.data ?? []}
            isLoading={tradesQ.isLoading}
            isError={tradesQ.isError}
            error={tradesQ.error}
            onFocus={handleFocus}
          />
          <PerformancePanel
            summary={perfQ.data}
            equityCurve={equityQ.data ?? []}
            isLoading={perfQ.isLoading}
            // Performance shows an error if EITHER the summary or the equity-curve query fails.
            isError={perfQ.isError || equityQ.isError}
            error={perfQ.error ?? equityQ.error}
          />
        </div>
      </div>
    </div>
  );
}
