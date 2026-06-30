# ---------------------------------------------------------------------------------------------------
# ICT Trading Desk — single-image build: the React SPA + the .NET 10 Host served single-origin.
# PAPER / READ-ONLY: the image runs the analysis + paper-trading Host only — no broker/order path (§6.3).
#
#   Stage 1 (web)     build the dashboard (VITE_USE_MOCKS=false) -> /web/dist
#   Stage 2 (build)   restore + publish the Host (+ all module projects) -> /publish
#   Stage 3 (runtime) aspnet base + /publish + the SPA in wwwroot; serves API + SignalR + SPA on :5080
# ---------------------------------------------------------------------------------------------------

# ---- Stage 1: build the React dashboard (live mode — talks to the same-origin API) ----
# node:24 matches the local toolchain (npm 11). NOT alpine: `npm ci` needs the lockfile to list every
# platform's optional deps, but the committed package-lock.json was generated on Windows and omits the
# Linux ones (@emnapi/*, the linux rollup binary). `npm install` resolves + fetches the right platform
# deps cross-platform, so the build is robust whatever host generated the lockfile.
FROM node:24-bookworm-slim AS web
WORKDIR /web
# Restore deps first (cached unless package.json / the lockfile changes).
COPY web/ict-dashboard/package.json web/ict-dashboard/package-lock.json ./
RUN npm install --no-audit --no-fund
# Then the source + a live (no-mocks) production build.
COPY web/ict-dashboard/ ./
RUN VITE_USE_MOCKS=false npm run build

# ---- Stage 2: restore + publish the .NET Host (modular monolith) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# The .dockerignore keeps the context lean (no bin/obj/node_modules/data/.git). Copy the whole tree so the
# Host's many ProjectReferences resolve, then publish (restore happens as part of publish).
COPY . .
RUN dotnet publish src/IctTrader.Host/IctTrader.Host.csproj -c Release -o /publish

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# The published Host (incl. appsettings.json + the module DLLs that carry the EF migrations).
COPY --from=build /publish ./
# The built SPA — served single-origin from wwwroot (app.UseStaticFiles + MapFallbackToFile in Program.cs).
COPY --from=web /web/dist ./wwwroot
# Bind all interfaces inside the container; compose maps the host port. Production env by default.
ENV ASPNETCORE_URLS=http://+:5080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 5080
# ContentRoot defaults to the WORKDIR (/app), so appsettings.json + wwwroot both resolve with no --contentRoot.
ENTRYPOINT ["dotnet", "IctTrader.Host.dll"]
