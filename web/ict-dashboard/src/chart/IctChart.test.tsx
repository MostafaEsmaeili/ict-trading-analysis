// IctChart mount test. lightweight-charts draws on a real canvas (jsdom has none), so we mock the
// library to a thin spy and assert the wrapper wires candles + overlays through the v5 API correctly:
// it creates a candlestick series, sets the candle data, registers markers, and draws price lines.
//
// vi.mock is hoisted above imports, so the factory must NOT close over outer variables. We define the
// spies inside the factory and reach them afterwards through the (mocked) module + the series instance
// the mocked createChart returns.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import * as lwc from 'lightweight-charts';

vi.mock('lightweight-charts', () => {
  const setData = vi.fn();
  const update = vi.fn();
  const applyOptions = vi.fn();
  const createPriceLine = vi.fn(() => ({ id: 'pl' }));
  const removePriceLine = vi.fn();
  const series = { setData, update, applyOptions, createPriceLine, removePriceLine };
  // Shared timeScale spies so fitContent/setVisibleRange call counts survive across renders.
  const fitContent = vi.fn();
  const setVisibleRange = vi.fn();
  const timeScale = () => ({ fitContent, setVisibleRange });
  return {
    createChart: vi.fn(() => ({
      addSeries: vi.fn(() => series),
      timeScale,
      remove: vi.fn(),
    })),
    createSeriesMarkers: vi.fn(() => ({ detach: vi.fn() })),
    CandlestickSeries: 'CandlestickSeries',
    ColorType: { Solid: 'solid' },
    LineStyle: { Solid: 0, Dotted: 1, Dashed: 2, LargeDashed: 3, SparseDotted: 4 },
    // Re-expose the shared timeScale spies for assertions.
    __timeScale: { fitContent, setVisibleRange },
  };
});

import { IctChart } from './IctChart';
import { MOCK_CANDLES, MOCK_OVERLAYS } from '../mocks/fixtures';
import type { CandleDto } from '../types/api';
import { defaultOverlayVisibility } from '../types/overlays';

/** Pull the single shared series instance back out of the mocked createChart. */
function mockedSeries() {
  const chart = vi.mocked(lwc.createChart).mock.results.at(-1)?.value as {
    addSeries: ReturnType<typeof vi.fn>;
  };
  return chart.addSeries.mock.results.at(-1)?.value as {
    setData: ReturnType<typeof vi.fn>;
    update: ReturnType<typeof vi.fn>;
    createPriceLine: ReturnType<typeof vi.fn>;
  };
}

function timeScaleSpies() {
  return (lwc as unknown as { __timeScale: { fitContent: ReturnType<typeof vi.fn>; setVisibleRange: ReturnType<typeof vi.fn> } }).__timeScale;
}

describe('IctChart', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('mounts, renders the chart surface, and feeds candles + overlays', () => {
    const { getByTestId } = render(
      <IctChart candles={MOCK_CANDLES} overlays={MOCK_OVERLAYS} visibility={defaultOverlayVisibility()} />,
    );

    expect(getByTestId('ict-chart')).toBeInTheDocument();
    expect(lwc.createChart).toHaveBeenCalledOnce();

    const series = mockedSeries();
    expect(series.setData).toHaveBeenCalledOnce();
    expect(series.setData.mock.calls[0][0]).toHaveLength(MOCK_CANDLES.length);
    expect(lwc.createSeriesMarkers).toHaveBeenCalled();
    expect(series.createPriceLine).toHaveBeenCalled();
  });

  it('draws no overlays when all toggles are off', () => {
    const allOff = Object.fromEntries(
      Object.keys(defaultOverlayVisibility()).map((k) => [k, false]),
    ) as ReturnType<typeof defaultOverlayVisibility>;

    render(<IctChart candles={MOCK_CANDLES} overlays={MOCK_OVERLAYS} visibility={allOff} />);
    expect(mockedSeries().createPriceLine).not.toHaveBeenCalled();
  });

  it('INITIAL data load calls setData + fitContent (full render), NOT series.update', () => {
    const vis = defaultOverlayVisibility();
    render(<IctChart candles={MOCK_CANDLES} overlays={[]} visibility={vis} />);

    const series = mockedSeries();
    const ts = timeScaleSpies();
    // The first-ever data for the series must render the WHOLE series and fit the time scale so all the
    // candles (and their overlays) are visible — the auto-scale-to-one-bar (~4-pip window) regression is
    // precisely the case where update() is wrongly used on first load.
    expect(series.setData).toHaveBeenCalledTimes(1);
    expect(series.setData.mock.calls[0][0]).toHaveLength(MOCK_CANDLES.length);
    expect(ts.fitContent).toHaveBeenCalledTimes(1);
    expect(series.update).not.toHaveBeenCalled();
  });

  it('uses series.update (not setData+fitContent) for an appended live bar', () => {
    const vis = defaultOverlayVisibility();
    const { rerender } = render(<IctChart candles={MOCK_CANDLES} overlays={[]} visibility={vis} />);

    const series = mockedSeries();
    const ts = timeScaleSpies();
    // Initial load: one setData + one fitContent.
    expect(series.setData).toHaveBeenCalledTimes(1);
    expect(ts.fitContent).toHaveBeenCalledTimes(1);
    expect(series.update).not.toHaveBeenCalled();

    // Append one new bar (new array reference, same symbol/timeframe, strictly-newer last bar).
    const last = MOCK_CANDLES[MOCK_CANDLES.length - 1];
    const appended: CandleDto = {
      ...last,
      openTimeUtc: new Date(Date.parse(last.openTimeUtc) + 5 * 60_000).toISOString(),
    };
    rerender(<IctChart candles={[...MOCK_CANDLES, appended]} overlays={[]} visibility={vis} />);

    // Incremental: update() called, NO second setData/fitContent (pan/zoom preserved).
    expect(series.update).toHaveBeenCalledTimes(1);
    expect(series.setData).toHaveBeenCalledTimes(1);
    expect(ts.fitContent).toHaveBeenCalledTimes(1);
  });

  it('re-feeds via setData (no update, no fitContent) when a bar is inserted MID-SERIES', () => {
    // An out-of-order / redelivered bar lands BEFORE the last bar (appendCandle branch 3): the last bar's
    // time is unchanged but the array grew, so series.update() would only re-push the unchanged last bar and
    // never render the inserted middle bar. The chart must fall back to setData() — without re-fitting, to
    // preserve the operator's pan/zoom.
    const vis = defaultOverlayVisibility();
    // Start with a 10-min gap between the last two bars so an earlier absent bar can slot in between.
    const base = MOCK_CANDLES.slice(0, -1);
    const last = base[base.length - 1];
    const gapped: CandleDto = {
      ...MOCK_CANDLES[MOCK_CANDLES.length - 1],
      openTimeUtc: new Date(Date.parse(last.openTimeUtc) + 10 * 60_000).toISOString(),
    };
    const initial = [...base, gapped];
    const { rerender } = render(<IctChart candles={initial} overlays={[]} visibility={vis} />);

    const series = mockedSeries();
    const ts = timeScaleSpies();
    expect(series.setData).toHaveBeenCalledTimes(1);
    expect(ts.fitContent).toHaveBeenCalledTimes(1);

    // Insert a bar 5 min after `last` (i.e. between `last` and `gapped`) — a mid-series insert.
    const middle: CandleDto = {
      ...last,
      openTimeUtc: new Date(Date.parse(last.openTimeUtc) + 5 * 60_000).toISOString(),
    };
    const merged = [...base, middle, gapped]; // last bar (gapped) unchanged; length grew by one
    rerender(<IctChart candles={merged} overlays={[]} visibility={vis} />);

    // setData called again (the inserted bar is rendered), update() never used, fitContent NOT re-called.
    expect(series.setData).toHaveBeenCalledTimes(2);
    expect(series.setData.mock.calls[1][0]).toHaveLength(merged.length);
    expect(series.update).not.toHaveBeenCalled();
    expect(ts.fitContent).toHaveBeenCalledTimes(1);
  });

  it('does a full setData+fitContent on a symbol switch (not an incremental update)', () => {
    const vis = defaultOverlayVisibility();
    const { rerender } = render(<IctChart candles={MOCK_CANDLES} overlays={[]} visibility={vis} />);
    const series = mockedSeries();
    const ts = timeScaleSpies();

    // Switch symbol → dataset identity changes → full reload.
    const other = MOCK_CANDLES.map((c) => ({ ...c, symbol: 'GBPUSD' }));
    rerender(<IctChart candles={other} overlays={[]} visibility={vis} />);

    expect(series.setData).toHaveBeenCalledTimes(2);
    expect(ts.fitContent).toHaveBeenCalledTimes(2);
    expect(series.update).not.toHaveBeenCalled();
  });

  it('re-renders fully (setData + fitContent) when the series is RECREATED (remount), not incrementally', () => {
    // The live regression: when the chart effect tears down and recreates the series (remount / StrictMode
    // double-invoke), the new series is EMPTY. The candle effect must NOT treat the same candles as an
    // incremental append against the stale (now-destroyed) series — it must do a full setData + fitContent,
    // else lightweight-charts auto-scales to a single pushed bar's ~4-pip range with no visible candles.
    const vis = defaultOverlayVisibility();
    const { unmount } = render(<IctChart candles={MOCK_CANDLES} overlays={[]} visibility={vis} />);

    const ts = timeScaleSpies();
    expect(ts.fitContent).toHaveBeenCalledTimes(1);
    unmount();

    // Mount a fresh instance with the SAME candles (same symbol|timeframe identity).
    render(<IctChart candles={MOCK_CANDLES} overlays={[]} visibility={vis} />);
    // NOTE: the lightweight-charts mock returns a SINGLE shared series object across all chart instances,
    // so its spy call-counts accumulate across both mounts. The remount must have done a FULL render: a
    // second setData of the whole series + a second fitContent, and update() must NEVER have fired (the
    // regression would have routed the remount through series.update on the new empty series → no fit).
    const series = mockedSeries();
    expect(series.setData).toHaveBeenCalledTimes(2);
    expect(series.setData.mock.calls.at(-1)?.[0]).toHaveLength(MOCK_CANDLES.length);
    expect(series.update).not.toHaveBeenCalled();
    expect(ts.fitContent).toHaveBeenCalledTimes(2); // one fit per mounted instance
  });

  it('seeks the visible range when a seekToUtc within the window is supplied', () => {
    const vis = defaultOverlayVisibility();
    const ts = timeScaleSpies();
    const mid = MOCK_CANDLES[Math.floor(MOCK_CANDLES.length / 2)].openTimeUtc;
    render(
      <IctChart candles={MOCK_CANDLES} overlays={[]} visibility={vis} seekToUtc={mid} />,
    );
    expect(ts.setVisibleRange).toHaveBeenCalledTimes(1);
  });
});
