// ---------------------------------------------------------------------------------------------------
// The app's React Query client factory (plan §9 — the dashboard's server-state owner). Kept in its own
// module so App.tsx exports only the <App/> component (Vite fast-refresh stays happy).
// ---------------------------------------------------------------------------------------------------

import { QueryClient } from '@tanstack/react-query';

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15_000,
        refetchOnWindowFocus: false,
        retry: 1,
      },
    },
  });
}
