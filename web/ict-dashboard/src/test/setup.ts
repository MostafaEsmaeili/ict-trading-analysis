// Vitest global setup — jest-dom matchers + jsdom shims that lightweight-charts/recharts need.
import '@testing-library/jest-dom/vitest';
import { afterEach, vi } from 'vitest';
import { cleanup, configure } from '@testing-library/react';

// Raise React Testing Library's async-utility timeout (default 1000ms). The heavy mock-driven async tests
// (optimizer grid sweep, recharts/lightweight-charts mounts) resolve their React Query data over several
// ticks; under full parallel CPU contention the 1s default is marginal, so a DIFFERENT test would
// intermittently time out per run while passing in isolation. This is RTL's own timeout, separate from
// vitest's testTimeout, so it must be set here for `waitFor`/`findBy*` to get the longer budget.
configure({ asyncUtilTimeout: 5000 });

afterEach(() => {
  cleanup();
});

// jsdom lacks ResizeObserver (recharts ResponsiveContainer + lightweight-charts autoSize use it).
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
globalThis.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver;

// jsdom lacks matchMedia.
globalThis.matchMedia ??= ((query: string) => ({
  matches: false,
  media: query,
  onchange: null,
  addEventListener: vi.fn(),
  removeEventListener: vi.fn(),
  addListener: vi.fn(),
  removeListener: vi.fn(),
  dispatchEvent: vi.fn(),
})) as unknown as typeof globalThis.matchMedia;
