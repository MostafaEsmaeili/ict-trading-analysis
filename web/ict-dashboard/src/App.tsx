// ---------------------------------------------------------------------------------------------------
// App — composition root (plan §15). Provides the React Query client (the dashboard's server-state
// owner, plan §9) and the router for the 6-page app: Live (/) · Signals (/signals) · Trades (/trades) ·
// Backtest (/backtest) · Optimizer (/optimizer) · Settings (/settings). The AppLayout shell holds the
// persistent nav + guardrail badge. Exported separately from main.tsx so tests can mount it.
// ---------------------------------------------------------------------------------------------------

import { QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AppLayout } from './components/AppLayout';
import { Dashboard } from './Dashboard';
import { SignalsPage } from './pages/SignalsPage';
import { TradesPage } from './pages/TradesPage';
import { BacktestPage } from './pages/BacktestPage';
import { OptimizerPage } from './pages/OptimizerPage';
import { SettingsPage } from './pages/SettingsPage';
import { createQueryClient } from './queryClient';

// One client per app instance (created once, not per render).
const appQueryClient = createQueryClient();

export function App(): React.JSX.Element {
  return (
    <QueryClientProvider client={appQueryClient}>
      <BrowserRouter>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/" element={<Dashboard />} />
            <Route path="/signals" element={<SignalsPage />} />
            <Route path="/trades" element={<TradesPage />} />
            <Route path="/backtest" element={<BacktestPage />} />
            <Route path="/optimizer" element={<OptimizerPage />} />
            <Route path="/settings" element={<SettingsPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
