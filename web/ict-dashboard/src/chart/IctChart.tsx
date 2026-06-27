// ---------------------------------------------------------------------------------------------------
// IctChart — the ICT Pattern Chart centerpiece (plan §9.1) on TradingView lightweight-charts v5 (free
// OSS, Apache-2.0). Renders candles for the selected symbol/timeframe and draws each detected ICT
// concept as a toggleable overlay (the §9.1 table): FVG / OB + 50% / liquidity / sweep / MSS / OTE band
// + 70.5% / killzone band / entry-stop-T1-T2 price lines with R labels / draw-on-liquidity.
//
// Read-only analysis only — there is no order/execute interaction anywhere (plan §6.3). Markers use the
// series-markers plugin; horizontal levels use price lines; zones/bands are scaffolded as price lines at
// their bounds (full translucent-rectangle primitives are a follow-on, tracked in the legend).
// ---------------------------------------------------------------------------------------------------

import { useEffect, useMemo, useRef } from 'react';
import {
  CandlestickSeries,
  ColorType,
  LineStyle,
  createChart,
  createSeriesMarkers,
  type CandlestickData,
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type SeriesMarker,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import type { CandleDto } from '../types/api';
import type { ChartOverlay, OverlayVisibility } from '../types/overlays';
import { palette } from '../theme';
import { toUtcTimestamp } from '../time';
import { priceDecimals } from '../format';

export interface IctChartProps {
  candles: CandleDto[];
  overlays: ChartOverlay[];
  visibility: OverlayVisibility;
  /**
   * Optional UTC instant to bring into view (focus-on-alert/trade). When set, the chart seeks the time
   * scale around it instead of re-fitting the whole window. If the instant is outside the loaded window
   * the seek is a best-effort no-op (the symbol still switches) — older bars aren't fetched here.
   */
  seekToUtc?: string;
}

function toCandlestickData(c: CandleDto): CandlestickData<Time> {
  return {
    time: toUtcTimestamp(c.openTimeUtc) as UTCTimestamp,
    open: c.open,
    high: c.high,
    low: c.low,
    close: c.close,
  };
}

/** Half-width (in bars) of the visible window when seeking to a focused setup's time. */
const SEEK_WINDOW_BARS = 30;

const CHART_OPTIONS = {
  layout: {
    background: { type: ColorType.Solid, color: palette.panel },
    textColor: palette.textMuted,
    fontFamily: "'JetBrains Mono', 'Consolas', ui-monospace, monospace",
    attributionLogo: false,
  },
  grid: {
    vertLines: { color: palette.border },
    horzLines: { color: palette.border },
  },
  timeScale: { borderColor: palette.borderStrong, timeVisible: true, secondsVisible: false },
  rightPriceScale: { borderColor: palette.borderStrong },
  crosshair: { mode: 0 as const },
  autoSize: true,
} as const;

export function IctChart({
  candles,
  overlays,
  visibility,
  seekToUtc,
}: IctChartProps): React.JSX.Element {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  // Track the last-rendered dataset identity (symbol|timeframe) + the last bar time so a single live
  // append/forming-bar update goes through series.update() (preserving pan/zoom) while a full dataset
  // replacement (initial load / symbol or timeframe switch) does setData()+fitContent() exactly once.
  const lastSeriesKeyRef = useRef<string | undefined>(undefined);
  const lastBarTimeRef = useRef<number | undefined>(undefined);
  // Track the array length alongside the last bar time so a MID-SERIES insert/upsert (out-of-order /
  // redelivered bar — appendCandle branch 2/3, where the LAST bar is unchanged) is distinguished from a
  // pure forming-bar-in-place update and re-fed via setData() instead of being missed by series.update().
  const lastLenRef = useRef<number | undefined>(undefined);
  // Has the CURRENT series instance been populated with setData() yet? The incremental series.update()
  // fast-path is only legal against an ALREADY-RENDERED series — a fresh (or recreated) empty series MUST
  // take the full setData()+fitContent() path or lightweight-charts auto-scales to the single pushed bar's
  // tiny range (the ~4-pip no-candles regression). This is reset to false whenever the series is (re)created
  // below, so a remount / StrictMode double-invoke can never mistake an empty series for an incremental one.
  const seriesRenderedRef = useRef(false);

  // Create the chart + candlestick series once.
  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }
    const chart = createChart(container, CHART_OPTIONS);
    const series = chart.addSeries(CandlestickSeries, {
      upColor: palette.long,
      downColor: palette.short,
      borderUpColor: palette.long,
      borderDownColor: palette.short,
      wickUpColor: palette.long,
      wickDownColor: palette.short,
      // FX-major default; the candle-feed effect re-applies the per-symbol precision below.
      priceFormat: { type: 'price', precision: 5, minMove: 0.00001 },
    });
    chartRef.current = chart;
    seriesRef.current = series;
    // A brand-new (empty) series — force the next candle effect down the full setData()+fitContent() path
    // (and clear the stale last-bar/length/key trackers from the prior instance) so it can't be mistaken
    // for an incremental append against a series that has no data yet.
    seriesRenderedRef.current = false;
    lastSeriesKeyRef.current = undefined;
    lastBarTimeRef.current = undefined;
    lastLenRef.current = undefined;

    return () => {
      chart.remove();
      chartRef.current = null;
      seriesRef.current = null;
      seriesRenderedRef.current = false;
    };
  }, []);

  // Feed candles. A full dataset replacement (initial/first load, a recreated series, or a symbol/timeframe
  // switch) calls setData() + fitContent() once and re-applies the per-symbol price precision (JPY → 3,
  // metals → 2, indices → 1, FX majors → 5; the wire carries no precision field). A single live append or
  // forming-bar update against an already-rendered series uses series.update() so the operator's pan/zoom
  // survives the tick (lightweight-charts update() upserts by time and does NOT touch the time scale).
  useEffect(() => {
    const series = seriesRef.current;
    if (!series) {
      return;
    }

    const first = candles[0];
    const symbol = first?.symbol;
    const seriesKey = first ? `${first.symbol}|${first.timeframe}` : undefined;
    // A FULL render (setData + fitContent) is required when the series hasn't been rendered yet (first-ever
    // data / a recreated empty series) OR the dataset identity changed (symbol|timeframe switch). Anything
    // else is an in-place change to an already-rendered, same-identity series.
    const needsFullRender = !seriesRenderedRef.current || seriesKey !== lastSeriesKeyRef.current;

    if (symbol && needsFullRender) {
      const decimals = priceDecimals(symbol);
      series.applyOptions({
        priceFormat: { type: 'price', precision: decimals, minMove: 10 ** -decimals },
      });
    }

    const lastBar = candles[candles.length - 1];
    const lastTime = lastBar ? (toUtcTimestamp(lastBar.openTimeUtc) as number) : undefined;

    if (needsFullRender) {
      // First data for this series, or a symbol/timeframe switch: render the WHOLE series and fit the time
      // scale so all 500 candles + their overlays are visible (the restored initial fit). An empty candles
      // array still resets the series cleanly and is not treated as "rendered" (it cannot fit nothing).
      series.setData(candles.map(toCandlestickData));
      if (lastBar) {
        chartRef.current?.timeScale().fitContent();
        seriesRenderedRef.current = true;
      }
    } else if (lastBar && lastTime !== undefined && lastBarTimeRef.current !== undefined) {
      // Already-rendered, same-identity series. The incremental series.update() fast-path applies ONLY to a
      // true last-bar move: a strict append of one-or-more newer bars (lastTime > last), OR a pure
      // forming-bar-in-place update (lastTime == last AND the length is unchanged). A MID-SERIES
      // insert/upsert (appendCandle branch 2/3 — an out-of-order / redelivered bar lands before the last
      // one, so lastTime is unchanged but the length grew or a middle bar's content shifted) is NOT
      // incremental: update() would only re-push the unchanged last bar and the inserted/edited middle bar
      // would never reach lightweight-charts. Those re-feed via setData() WITHOUT fitContent() so the
      // operator's pan/zoom survives (consistent with update()).
      const isPureAppend = lastTime > lastBarTimeRef.current;
      const isFormingBar = lastTime === lastBarTimeRef.current && candles.length === lastLenRef.current;
      if (isPureAppend || isFormingBar) {
        series.update(toCandlestickData(lastBar));
      } else {
        series.setData(candles.map(toCandlestickData));
      }
    } else if (lastBar) {
      // Same-identity series whose first data arrives here as a non-append edge (no prior last-bar time):
      // render without re-fitting (defensive — the needsFullRender branch normally owns first data).
      series.setData(candles.map(toCandlestickData));
    }

    lastSeriesKeyRef.current = seriesKey;
    lastBarTimeRef.current = lastTime;
    lastLenRef.current = candles.length;
  }, [candles]);

  // Seek the time scale to a focus instant (focus-on-alert/trade), keyed on [seekToUtc, candles]. Runs
  // after the candle effect (declaration order). When a seek target is set we center the visible range
  // around it instead of leaving the chart fit to the full window. Best-effort: an out-of-window instant
  // (no surrounding bars) leaves the view as-is — the symbol switch already happened upstream.
  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || !seekToUtc || candles.length === 0) {
      return;
    }
    const target = toUtcTimestamp(seekToUtc);
    // Window the view to ~SEEK_WINDOW_BARS each side of the target at the data's own bar spacing.
    const barSec =
      candles.length >= 2
        ? Math.max(1, toUtcTimestamp(candles[1].openTimeUtc) - toUtcTimestamp(candles[0].openTimeUtc))
        : 60;
    const from = candles[0].openTimeUtc;
    const to = candles[candles.length - 1].openTimeUtc;
    const fromSec = toUtcTimestamp(from);
    const toSec = toUtcTimestamp(to);
    // Only seek when the instant is within (or near) the loaded window — else there is nothing to show.
    if (target < fromSec - barSec || target > toSec + barSec) {
      return;
    }
    const half = SEEK_WINDOW_BARS * barSec;
    chart.timeScale().setVisibleRange({
      from: Math.max(fromSec, target - half) as UTCTimestamp,
      to: Math.min(toSec, target + half) as UTCTimestamp,
    });
  }, [seekToUtc, candles]);

  // Visible overlays (respecting the legend toggles).
  const visibleOverlays = useMemo(
    () => overlays.filter((o) => visibility[o.kind]),
    [overlays, visibility],
  );

  // Draw overlays as markers (sweep/MSS) + price lines (levels/zones/bands/liquidity/draw).
  useEffect(() => {
    const chart = chartRef.current;
    const series = seriesRef.current;
    if (!chart || !series) {
      return;
    }

    const markers: SeriesMarker<Time>[] = [];
    const priceLines: IPriceLine[] = [];

    const line = (
      price: number,
      color: string,
      title: string,
      style: LineStyle = LineStyle.Solid,
      width: 1 | 2 = 1,
    ): void => {
      priceLines.push(
        series.createPriceLine({
          price,
          color,
          lineWidth: width,
          lineStyle: style,
          axisLabelVisible: true,
          title,
        }),
      );
    };

    for (const o of visibleOverlays) {
      switch (o.kind) {
        case 'sweep':
          markers.push({
            time: toUtcTimestamp(o.atUtc) as UTCTimestamp,
            position: o.direction === 'Bullish' ? 'belowBar' : 'aboveBar',
            color: palette.pending,
            shape: o.direction === 'Bullish' ? 'arrowUp' : 'arrowDown',
            text: 'sweep',
          });
          break;
        case 'mss':
          markers.push({
            time: toUtcTimestamp(o.atUtc) as UTCTimestamp,
            position: o.direction === 'Bullish' ? 'belowBar' : 'aboveBar',
            color: palette.accent,
            shape: o.direction === 'Bullish' ? 'arrowUp' : 'arrowDown',
            text: 'MSS',
          });
          line(o.brokenSwingPrice, palette.accent, 'MSS swing', LineStyle.Dashed);
          break;
        case 'liquidity':
          line(
            o.price,
            o.side === 'buy' ? palette.long : palette.short,
            `${o.label}${o.swept ? ' (swept)' : ''}`,
            LineStyle.LargeDashed,
          );
          break;
        case 'fvg':
          line(o.top, o.mitigated ? palette.neutral : palette.long, 'FVG top', LineStyle.Dotted);
          line(o.bottom, o.mitigated ? palette.neutral : palette.long, 'FVG bottom', LineStyle.Dotted);
          break;
        case 'orderBlock':
          line(o.top, palette.entry, o.isBreaker ? 'BRK top' : 'OB top', LineStyle.Dotted);
          line(o.bottom, palette.entry, o.isBreaker ? 'BRK bottom' : 'OB bottom', LineStyle.Dotted);
          line(o.meanThreshold, palette.entry, 'OB 50%', LineStyle.Dashed);
          break;
        case 'ote':
          line(o.band62, palette.ote, 'OTE 62%', LineStyle.Dotted);
          line(o.band79, palette.ote, 'OTE 79%', LineStyle.Dotted);
          line(o.sweetSpot705, palette.ote, 'OTE 70.5%', LineStyle.Solid);
          break;
        case 'drawOnLiquidity':
          line(o.targetPrice, palette.pending, 'Draw', LineStyle.Dashed);
          break;
        case 'tradeLevels': {
          line(o.entry, palette.entry, 'Entry', LineStyle.Solid, 2);
          line(o.stop, palette.short, 'Stop (1R)', LineStyle.Solid, 2);
          // Each target sits at its own distance from entry, so its R differs — compute per target
          // (R = |target − entry| / |entry − stop|, the frozen 1R) instead of repeating the plan RR.
          const riskPerUnit = Math.abs(o.entry - o.stop);
          o.targets.forEach((tp, i) => {
            const r = riskPerUnit > 0 ? Math.abs(tp - o.entry) / riskPerUnit : 0;
            line(tp, palette.long, `T${i + 1} · ${r.toFixed(1)}R`, LineStyle.Dashed, 2);
          });
          break;
        }
        case 'killzone':
          // Vertical session band is a primitive follow-on; the killzone is conveyed via the chart
          // header badge + the legend for now (every overlay also appears as alert text — §9.1 a11y).
          break;
      }
    }

    const markersPlugin = createSeriesMarkers(series, markers);

    return () => {
      markersPlugin.detach();
      for (const pl of priceLines) {
        series.removePriceLine(pl);
      }
    };
  }, [visibleOverlays]);

  return <div ref={containerRef} data-testid="ict-chart" className="chart-surface" />;
}
