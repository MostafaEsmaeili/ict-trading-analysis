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

export interface IctChartProps {
  candles: CandleDto[];
  overlays: ChartOverlay[];
  visibility: OverlayVisibility;
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

export function IctChart({ candles, overlays, visibility }: IctChartProps): React.JSX.Element {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);

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
      priceFormat: { type: 'price', precision: 5, minMove: 0.00001 },
    });
    chartRef.current = chart;
    seriesRef.current = series;

    return () => {
      chart.remove();
      chartRef.current = null;
      seriesRef.current = null;
    };
  }, []);

  // Feed candles.
  useEffect(() => {
    const series = seriesRef.current;
    if (!series) {
      return;
    }
    series.setData(candles.map(toCandlestickData));
    chartRef.current?.timeScale().fitContent();
  }, [candles]);

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
        case 'tradeLevels':
          line(o.entry, palette.entry, 'Entry', LineStyle.Solid, 2);
          line(o.stop, palette.short, 'Stop (1R)', LineStyle.Solid, 2);
          o.targets.forEach((tp, i) =>
            line(tp, palette.long, `T${i + 1} · ${o.rewardRatio.toFixed(1)}R`, LineStyle.Dashed, 2),
          );
          break;
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
