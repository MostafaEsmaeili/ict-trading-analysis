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
  const createPriceLine = vi.fn(() => ({ id: 'pl' }));
  const removePriceLine = vi.fn();
  const series = { setData, createPriceLine, removePriceLine };
  return {
    createChart: vi.fn(() => ({
      addSeries: vi.fn(() => series),
      timeScale: () => ({ fitContent: vi.fn() }),
      remove: vi.fn(),
    })),
    createSeriesMarkers: vi.fn(() => ({ detach: vi.fn() })),
    CandlestickSeries: 'CandlestickSeries',
    ColorType: { Solid: 'solid' },
    LineStyle: { Solid: 0, Dotted: 1, Dashed: 2, LargeDashed: 3, SparseDotted: 4 },
  };
});

import { IctChart } from './IctChart';
import { MOCK_CANDLES, MOCK_OVERLAYS } from '../mocks/fixtures';
import { defaultOverlayVisibility } from '../types/overlays';

/** Pull the single shared series instance back out of the mocked createChart. */
function mockedSeries() {
  const chart = vi.mocked(lwc.createChart).mock.results.at(-1)?.value as {
    addSeries: ReturnType<typeof vi.fn>;
  };
  return chart.addSeries.mock.results.at(-1)?.value as {
    setData: ReturnType<typeof vi.fn>;
    createPriceLine: ReturnType<typeof vi.fn>;
  };
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
});
