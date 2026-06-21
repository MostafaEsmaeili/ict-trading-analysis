// Shared lightweight-charts mock factory for component tests (jsdom has no canvas). Returns the module
// shape the IctChart wrapper consumes via the v5 API (addSeries / createSeriesMarkers / price lines).
import { vi } from 'vitest';

export function lightweightChartsMock() {
  return {
    createChart: vi.fn(() => ({
      addSeries: vi.fn(() => ({
        setData: vi.fn(),
        createPriceLine: vi.fn(() => ({ id: 'pl' })),
        removePriceLine: vi.fn(),
      })),
      timeScale: () => ({ fitContent: vi.fn() }),
      remove: vi.fn(),
    })),
    createSeriesMarkers: vi.fn(() => ({ detach: vi.fn() })),
    CandlestickSeries: 'CandlestickSeries',
    ColorType: { Solid: 'solid' },
    LineStyle: { Solid: 0, Dotted: 1, Dashed: 2, LargeDashed: 3, SparseDotted: 4 },
  };
}
