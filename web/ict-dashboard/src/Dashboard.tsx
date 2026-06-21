// ---------------------------------------------------------------------------------------------------
// Dashboard — the three-panel ICT trading-desk shell (plan §9): center ICT Pattern Chart, left Alerts
// Feed, right/bottom Active Paper Trades + Performance. This component is PRESENTATIONAL: all
// orchestration lives in custom hooks (useMarketSelection / useOverlayVisibility / useNyClock /
// useDashboardData per the repo guideline). React Query owns server state; SignalR deltas merge into
// the same keys via useTradingHub (inert until a hub is supplied — WP7).
//
// DEFENSIVE: read-only analysis only. There is NO execute/go-live/order control anywhere — the header
// carries an "Advisory / Paper" posture badge instead (plan §6.3).
// ---------------------------------------------------------------------------------------------------

import { AlertsFeed } from './components/AlertsFeed';
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
          onFocusSymbol={setSymbol}
        />

        <ChartPanel
          symbol={symbol}
          timeframe={timeframe}
          style={style}
          candles={candlesQ.data ?? []}
          overlays={overlaysQ.data ?? []}
          visibility={visibility}
          isLoading={candlesQ.isLoading}
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
            onFocusSymbol={setSymbol}
          />
          <PerformancePanel
            summary={perfQ.data}
            equityCurve={equityQ.data ?? []}
            isLoading={perfQ.isLoading}
          />
        </div>
      </div>
    </div>
  );
}
