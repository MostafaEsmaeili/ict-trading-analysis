---
name: add-vertical-slice
description: Procedure to add a module use-case to the modular monolith — a feature in Modules/<Module>/ wired on the in-memory IMessageBus (command/query/event handler), DTOs in the module's Contracts, a thin minimal-API endpoint, and a slice test — while ALL business logic stays in rich domain aggregates/value objects/domain services. NO MediatR. Use when adding any Application feature. Forbids generic repositories, anemic models, and cross-module internal references.
allowed-tools: Read Write Edit Grep Glob Bash(dotnet *)
---
# Add a module use-case (modular monolith on the outside, DDD on the inside)
The handler ORCHESTRATES; the DOMAIN DECIDES. Never put a business rule in a handler. NO MediatR — use
the project's in-memory `IMessageBus` (plan §3.0a).

1. Create the feature under `src/Modules/<Module>/Application/Features/<Name>/` (group by MODULE then FEATURE).
2. Define the message + handler: a `record <Name>Command : ICommand` (or `IQuery<T>` / `IEvent`) and its
   `ICommandHandler<>` / `IQueryHandler<,>` / `IEventHandler<>`. The handler loads the aggregate via its
   repository interface, calls a **domain method** that enforces the invariant and raises domain events,
   persists, and maps to a DTO. No `if`-business-logic in the handler.
3. **DDD discipline:**
   - Logic lives in **rich aggregates** (`PaperTrade`, `ScanSession`, `PaperAccount`) with private setters
     and intention-revealing methods (`Open`, `RegisterFill`, `Close`) — no anemic models.
   - Quantities are **value objects** (`Price`, `Pips`, `RiskPercent`, `RewardRatio`, `OteZone`) that
     self-validate; use the ubiquitous language from `ict-methodology`.
   - Cross-aggregate calculations are **domain services** (`IRiskManager`, `SetupScorer`,
     `PerformanceCalculator`, `IExecutionCostModel`) in the Domain project.
   - Side effects (alerts, SignalR, persistence) react to **domain events** published on the bus, never inline.
   - Respect aggregate boundaries: reference other aggregates by id; one transaction per aggregate.
4. **Module boundaries:** the module references only `SharedKernel`, `Domain`, its own `Contracts`, and other
   modules' `*.Contracts` — NEVER another module's Application/Infrastructure (enforced by the
   architecture tests in `IctTrader.ArchitectureTests`).
5. **Clean code:** no magic numbers → validated Options (`Ict:*`); no magic strings → `.resx` resources.
   Any new NuGet package is pinned to the **latest stable** version (the newest free/OSS version if the
   latest is commercially licensed — e.g. FluentAssertions 7.x, never MediatR).
6. Add a `FluentValidation` validator (input shape only — not business rules); it runs as a bus decorator.
7. Put DTOs in the module's `Contracts` (camelCase JSON); if they cross to the frontend, keep
   `web/ict-dashboard/src/types/api.ts` in sync.
8. Add a thin minimal-API endpoint in `IctTrader.Host` that only sends the bus message and maps the result.
9. Test: a focused slice test (integration against Testcontainers Postgres if it touches the DB) + pure
   unit tests for the domain method/value object. Run green before reporting done.
