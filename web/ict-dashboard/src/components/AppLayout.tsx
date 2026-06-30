// ---------------------------------------------------------------------------------------------------
// AppLayout — the persistent shell for the 4-page app (plan §15). Renders the shared NavBar (brand +
// nav + the "Advisory · Paper only" guardrail badge + NY clock) above the routed page <Outlet>. The
// badge stays visible on every route (the defensive posture surfaced at the UI, §6.3).
// ---------------------------------------------------------------------------------------------------

import { Outlet } from 'react-router-dom';
import { NavBar } from './NavBar';
import { ToastViewport } from '../notifications/ToastViewport';

export function AppLayout(): React.JSX.Element {
  return (
    <div className="app">
      <NavBar />
      <main className="app__main">
        <Outlet />
      </main>
      {/* One toast overlay for the whole app — closable notices (the operator's #1 complaint). */}
      <ToastViewport />
    </div>
  );
}
