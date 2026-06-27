// Test helper — mount a page/component inside the React Query + Router providers the app uses, so the
// hooks (useQuery/useMutation, useNavigate/useSearchParams) resolve exactly as they do at runtime.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { render, type RenderResult } from '@testing-library/react';
import type { ReactNode } from 'react';

export function makeTestClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, refetchOnWindowFocus: false, staleTime: Infinity },
      mutations: { retry: false },
    },
  });
}

/**
 * Render `ui` under the QueryClient + a MemoryRouter. When `path` is set, the element is mounted on a
 * matching route (so a deep-link `?symbol=` / drill-in route can be exercised); otherwise it is the
 * index route at `initialEntries[0]`.
 */
export function renderWithProviders(
  ui: ReactNode,
  options: { initialEntries?: string[]; path?: string } = {},
): RenderResult {
  const { initialEntries = ['/'], path } = options;
  const client = makeTestClient();
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={initialEntries}>
        {path ? (
          <Routes>
            <Route path={path} element={ui} />
          </Routes>
        ) : (
          ui
        )}
      </MemoryRouter>
    </QueryClientProvider>,
  );
}
