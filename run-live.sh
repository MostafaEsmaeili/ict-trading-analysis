#!/usr/bin/env bash
# Launch the ICT Trading Desk Host on the LIVE OANDA feed using local .env.
#
#   .env (gitignored) holds your OANDA practice token + the live-feed config — copy .env.example to
#   .env and fill in Ict__MarketData__Oanda__Token. This script sources it and boots the Host single-
#   origin (API + SignalR + the built SPA) at the ASPNETCORE_URLS in .env (default http://localhost:5080).
#
# Prereqs: Postgres up (`docker compose up -d postgres`), migrations applied, and the Host built
#   (`dotnet build src/IctTrader.Host -c Release`) + the SPA staged into wwwroot for single-origin
#   (`cd web/ict-dashboard && VITE_USE_MOCKS=false npm run build && cp -r dist/* ../../src/IctTrader.Host/wwwroot/`).
#
# Usage:  bash run-live.sh        (run from the repo root, in Git Bash / a POSIX shell)
#
# PAPER / READ-ONLY: the OANDA feed only GETs candles from the practice host — no order path.
set -euo pipefail
cd "$(dirname "$0")"

if [ ! -f .env ]; then
  echo "ERROR: .env not found. Copy .env.example to .env and add your OANDA practice token." >&2
  exit 1
fi

# Export every KEY=VALUE in .env as an environment variable for the Host.
set -a
# shellcheck disable=SC1091
. ./.env
set +a

if [ -z "${Ict__MarketData__Oanda__Token:-}" ] || [ "${Ict__MarketData__Oanda__Token}" = "your-oanda-practice-token-here" ]; then
  echo "ERROR: Ict__MarketData__Oanda__Token is unset/placeholder in .env. Add your OANDA practice token." >&2
  exit 1
fi

DLL="src/IctTrader.Host/bin/Release/net10.0/IctTrader.Host.dll"
if [ ! -f "$DLL" ]; then
  echo "ERROR: $DLL not found. Build first: dotnet build src/IctTrader.Host -c Release" >&2
  exit 1
fi

echo "Starting Host on ${ASPNETCORE_URLS:-http://localhost:5080} (provider=${Ict__MarketData__Provider:-?}, styles=${Ict__Scanning__ActiveStyles__0:-?}..., entry=${Ict__PaperTrading__DefaultEntryMode:-?})"
exec dotnet "$DLL" --contentRoot "$(pwd)/src/IctTrader.Host"
