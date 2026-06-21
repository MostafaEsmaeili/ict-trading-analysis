// ---------------------------------------------------------------------------------------------------
// Dashboard — the three-panel ICT trading-desk shell (plan §9): center ICT Pattern Chart, left Alerts
// Feed, right/bottom Active Paper Trades + Performance. React Query owns server state (hooks.ts);
// SignalR deltas merge into the same keys via useTradingHub (inert until a hub is supplied — WP7).
//
// DEFENSIVE: read-only analysis only. There is NO execute/go-live/order control anywhere — the header
// carries an "Advisory / Paper" posture badge instead (plan §6.3).
// ---------------------------------------------------------------------------------------------------

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useActiveTrades, useAlerts, useCandles, useEquityCurve, useOverlays, usePerformance } from './api/hooks';
import { useTradingHub } from './api/useTradingHub';
import { AlertsFeed } from './components/AlertsFeed';
import { ActivePaperTrades } from './components/ActivePaperTrades';
import { ChartPanel, STYLES } from './components/ChartPanel';
import { PerformancePanel } from './components/PerformancePanel';
import { defaultOverlayVisibility, type OverlayKind } from './types/overlays';
import type { TradeStyle } from './types/api';
import { nyClockLabel } from './time';
import { MOCK_SYMBOL, MOCK_TIMEFRAME } from './mocks/fixtures';

export function Dashboard(): React.JSX.Element {
  const [symbol, setSymbol] = useState<string>(MOCK_SYMBOL);
  const [timeframe, setTimeframe] = useState<string>(MOCK_TIMEFRAME);
  const [style, setStyle] = useState<TradeStyle>('Intraday');
  const [visibility, setVisibility] = useState(defaultOverlayVisibility);
  const [clock, setClock] = useState(() => nyClockLabel(new Date()));

  // NY desk clock (plan §9 — times default to New York).
  useEffect(() => {
    const id = setInterval(() => setClock(nyClockLabel(new Date())), 1000);
    return () => clearInterval(id);
  }, []);

  const candlesQ = useCandles(symbol, timeframe, style);
  const overlaysQ = useOverlays(symbol, timeframe);
  const alertsQ = useAlerts();
  const tradesQ = useActiveTrades();
  const perfQ = usePerformance();
  const equityQ = useEquityCurve();

  // SignalR wiring — inert without a live hub; the host connection lands in WP7.
  useTradingHub({ symbol, timeframe });

  const toggleOverlay = useCallback((kind: OverlayKind) => {
    setVisibility((v) => ({ ...v, [kind]: !v[kind] }));
  }, []);

  const toggleStyle = useCallback((next: TradeStyle) => {
    // Guard against a stray non-frozen style string.
    if (STYLES.includes(next)) {
      setStyle(next);
    }
  }, []);

  // Trigger TF for the header badge: the trigger timeframe of the latest alert on the focused symbol.
  const triggerTimeframe = useMemo(() => {
    const latest = (alertsQ.data ?? []).find((a) => a.symbol === symbol);
    return latest ? timeframe : null;
  }, [alertsQ.data, symbol, timeframe]);

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
          activeKillzone={(alertsQ.data ?? []).find((a) => a.symbol === symbol)?.killzone ?? null}
          triggerTimeframe={triggerTimeframe}
          onSymbolChange={setSymbol}
          onTimeframeChange={setTimeframe}
          onStyleChange={toggleStyle}
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
