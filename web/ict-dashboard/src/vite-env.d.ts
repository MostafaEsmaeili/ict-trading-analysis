/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base URL of the .NET host for REST + SignalR (empty in dev → uses the Vite proxy). */
  readonly VITE_API_BASE?: string;
  /** "false" opts into live fetches; anything else keeps the mock fixtures (default). */
  readonly VITE_USE_MOCKS?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
