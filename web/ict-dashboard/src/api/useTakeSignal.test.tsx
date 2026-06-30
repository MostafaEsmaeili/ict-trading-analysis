// useTakeSignal — the manual TAKE workflow mutation. On a 200 (Immediate) it merges the opened paper trade
// into the active-trades cache, flips the signal's IsTaken in the signals cache, and fires a tradeOpened
// notification; a 409 (already taken/expired) surfaces the `{ error }` reason as the mutation error.
// Mock-first (VITE_USE_MOCKS defaults true), so it drives the real mock client + the fixture take handler.
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { createElement } from 'react';
import { useTakeSignal } from './hooks';
import { queryKeys } from './queryKeys';
import { MOCK_SIGNALS, __resetMockSignalsForTest } from '../mocks/fixtures';
import {
  __resetNotificationsForTest,
  getSnapshot,
} from '../notifications/notificationStore';
import type { PaperTradeDto, RankedSignalDto } from '../types/api';

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => {
  __resetMockSignalsForTest();
  __resetNotificationsForTest();
});
afterEach(() => {
  __resetMockSignalsForTest();
  __resetNotificationsForTest();
});

describe('useTakeSignal', () => {
  it('200 (Immediate) merges the opened trade, flips the signal taken, and fires a tradeOpened toast', async () => {
    const qc = new QueryClient();
    // Seed the signals cache so the onSuccess flip + the take handler operate on the same Manual signal.
    qc.setQueryData<RankedSignalDto[]>(queryKeys.signals(), MOCK_SIGNALS.map((s) => ({ ...s })));

    const { result } = renderHook(() => useTakeSignal(), { wrapper: makeWrapper(qc) });

    const takeableId = MOCK_SIGNALS[0].setup.id; // rank 1 EURUSD Manual takeable
    result.current.mutate({ setupId: takeableId });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // The opened trade landed in the active-trades cache.
    const trades = qc.getQueryData<PaperTradeDto[]>(queryKeys.activeTrades());
    expect(trades?.some((t) => t.setupId === takeableId)).toBe(true);

    // The signal flipped to taken in the signals cache (its Take button would disable).
    const signals = qc.getQueryData<RankedSignalDto[]>(queryKeys.signals());
    expect(signals?.find((s) => s.setup.id === takeableId)?.isTaken).toBe(true);

    // A tradeOpened notification fired.
    expect(getSnapshot().some((n) => n.kind === 'tradeOpened')).toBe(true);
  });

  it('202 (Armed — an Auto signal) flips taken but opens no trade and fires no toast', async () => {
    const qc = new QueryClient();
    qc.setQueryData<RankedSignalDto[]>(queryKeys.signals(), MOCK_SIGNALS.map((s) => ({ ...s })));
    const { result } = renderHook(() => useTakeSignal(), { wrapper: makeWrapper(qc) });

    const autoId = MOCK_SIGNALS[1].setup.id; // rank 2 NAS100 Auto → mock returns null trade (202-like)
    result.current.mutate({ setupId: autoId });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.trade).toBeNull();
    expect(qc.getQueryData<PaperTradeDto[]>(queryKeys.activeTrades())).toBeUndefined();
    expect(getSnapshot().some((n) => n.kind === 'tradeOpened')).toBe(false);
  });

  it('409 (already taken) surfaces the error and merges nothing', async () => {
    const qc = new QueryClient();
    qc.setQueryData<RankedSignalDto[]>(queryKeys.signals(), MOCK_SIGNALS.map((s) => ({ ...s })));
    const { result } = renderHook(() => useTakeSignal(), { wrapper: makeWrapper(qc) });

    const takenId = MOCK_SIGNALS[3].setup.id; // rank 4 already taken → the mock throws
    result.current.mutate({ setupId: takenId });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toMatch(/already taken/i);
    expect(qc.getQueryData<PaperTradeDto[]>(queryKeys.activeTrades())).toBeUndefined();
  });
});
