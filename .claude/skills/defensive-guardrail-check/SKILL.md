---
name: defensive-guardrail-check
description: The mechanical checklist that proves the no-live-trading guardrail holds — only SimulatedTradeExecutor implements ITradeExecutor, no broker/order-routing symbol exists anywhere, LiveTradingEnabled validates false at startup, and all market-data feeds are read-only/sandbox. Use before merging and in CI.
allowed-tools: Read Grep Glob Bash(dotnet test *)
---
# Defensive guardrail check (must all PASS)
1. **Single executor** — exactly one `ITradeExecutor` implementation, `SimulatedTradeExecutor`, and it
   writes only to our DB. (grep for `: ITradeExecutor`.)
2. **No order-routing API** — no broker order/buy/sell/placeOrder/execute-to-broker symbols; no OANDA
   `orders`/`trades` POST endpoints; the MT5 bridge exposes only subscribe + inbound tick/bar (no order send).
3. **LiveTradingEnabled** — defaults false; an `IValidateOptions<>` FAILS startup if ever true. Confirm
   the validator exists and is registered.
4. **Read-only feeds** — every `IMarketDataFeed` is `IsReadOnly = true`; credentials sandbox/practice only.
5. **Module boundaries** — the architecture tests pass (no cross-module internals; no MediatR; SharedKernel/
   Domain depend on nothing).
6. **Architecture test** — the CI architecture test asserting 1–5 exists and passes:
   `dotnet test tests/IctTrader.ArchitectureTests`.
7. **Advisory flag** — every `Setup` carries `IsAdvisoryOnly = true`; the SignalR contract has no
   "execute" message.
Report PASS/FAIL per item with file:line evidence. Any FAIL blocks merge.
