// ---------------------------------------------------------------------------------------------------
// App — composition root. Provides the React Query client (the dashboard's server-state owner, plan §9)
// and renders the Dashboard shell. Exported separately from main.tsx so tests can mount it.
// ---------------------------------------------------------------------------------------------------

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Dashboard } from './Dashboard';

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

// One client per app instance (created once, not per render).
const appQueryClient = createQueryClient();

export function App(): React.JSX.Element {
  return (
    <QueryClientProvider client={appQueryClient}>
      <Dashboard />
    </QueryClientProvider>
  );
}
