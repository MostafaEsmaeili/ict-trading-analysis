/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Vite + React (WP8). The dev server proxies the frozen REST + SignalR surface to the .NET host
// (plan §9 / §11.1 #6) so the dashboard talks to one origin in dev; live wiring lands in WP7.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5080', changeOrigin: true, ws: true },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    include: ['src/**/*.test.{ts,tsx}'],
    // Force the deterministic, offline mock data layer for the whole suite, independent of any local
    // `.env.local` (the operator may set VITE_USE_MOCKS=false there for live render-verification —
    // that must NOT make the unit suite hit the real host). client.test.ts overrides this per-test
    // via vi.stubEnv to exercise the live fetch path.
    env: { VITE_USE_MOCKS: 'true' },
  },
});
