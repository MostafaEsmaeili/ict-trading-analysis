// ---------------------------------------------------------------------------------------------------
// Dashboard — the LIVE page (plan §9 / §15): center ICT Pattern Chart, left Alerts Feed, right Active
// Paper Trades + the Live-Config / Account panel + Performance. This component is PRESENTATIONAL: all
// orchestration lives in custom hooks (useMarketSelection / useOverlayVisibility / useDashboardData per
// the repo guideline). React Query owns server state; SignalR deltas merge into the same keys via
// useTradingHub (live in a non-mocks build; mocks mode reconciles on the poll).
//
// The shared header (brand + nav + the "Advisory / Paper" posture badge + NY clock) now lives in the
// AppLayout shell so it persists across the 4 routes. DEFENSIVE: read-only analysis only — there is NO
// execute/go-live/order control anywhere (plan §6.3).
// ---------------------------------------------------------------------------------------------------

import { useCallback, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { AlertsFeed, type FocusTarget } from './components/AlertsFeed';
import { ActivePaperTrades } from './components/ActivePaperTrades';
import { ChartPanel } from './components/ChartPanel';
import { LiveConfigPanel } from './components/LiveConfigPanel';
import { MarketStatus } from './components/MarketStatus';
import { PerformancePanel } from './components/PerformancePanel';
import { TopSignalsPanel } from './components/TopSignalsPanel';
import { WinnerSignalCard } from './components/WinnerSignalCard';
import { useMarketSelection } from './hooks/useMarketSelection';
import { useOverlayVisibility } from './hooks/useOverlayVisibility';
import { useDashboardData } from './hooks/useDashboardData';
import { useAccountStatus, useConfig, useMarketStatus } from './api/hooks';

export function Dashboard(): React.JSX.Element {
  // A `?symbol=` deep-link (off the Trades page) seeds the initial chart symbol once.
  const [searchParams] = useSearchParams();
  const { symbol, timeframe, style, setSymbol, setTimeframe, selectStyle } = useMarketSelection(
    searchParams.get('symbol') ?? undefined,
  );
  const { visibility, toggleOverlay } = useOverlayVisibility();

  // Focus-on-alert/trade/signal: switch the symbol AND seek the chart to the clicked moment. Alerts/trades
  // carry no timeframe/style so the operator's selection is kept; a SIGNAL's DTO does carry them, so a
  // signal focus also switches the chart TF + style (target.timeframe / target.style when present).
  const [seekToUtc, setSeekToUtc] = useState<string | undefined>(undefined);
  const handleFocus = useCallback(
    (target: FocusTarget) => {
      setSymbol(target.symbol);
      if (target.timeframe) setTimeframe(target.timeframe);
      if (target.style) selectStyle(target.style);
      setSeekToUtc(target.atUtc);
    },
    [setSymbol, setTimeframe, selectStyle],
  );

  const { candlesQ, overlaysQ, alertsQ, tradesQ, perfQ, equityQ, activeKillzone, triggerTimeframe } =
    useDashboardData({ symbol, timeframe, style });

  const configQ = useConfig();
  const accountQ = useAccountStatus();
  const marketStatusQ = useMarketStatus();

  return (
    <div className="live-page">
      {/* The HERO card — the single best opportunity right now (the #1 ranked signal), leading the Live
          page as a full-width banner so it's the first thing the operator sees. Paper-only Take (§6.3). */}
      <WinnerSignalCard onFocus={handleFocus} />

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
        <MarketStatus
          status={marketStatusQ.data}
          fetchedAt={marketStatusQ.dataUpdatedAt || undefined}
          isLoading={marketStatusQ.isLoading}
          isError={marketStatusQ.isError}
          error={marketStatusQ.error}
        />
        <LiveConfigPanel
          config={configQ.data}
          account={accountQ.data}
          isLoading={configQ.isLoading || accountQ.isLoading}
          isError={configQ.isError || accountQ.isError}
          error={configQ.error ?? accountQ.error}
        />
        <TopSignalsPanel onFocus={handleFocus} />
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
