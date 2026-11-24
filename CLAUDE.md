# Engineering rules for AI agents

This file is loaded by Claude Code in every session. It defines how changes are made in this repository.
Hooks in `.claude/settings.json` enforce the parts that can be automated.

## Workflow
1. **Spec first.** A new behavior starts as a spec in `specs/NNN-name.md` (context, behavior, acceptance criteria, out of scope).
2. **Small steps.** One concern per commit, [Conventional Commits](https://www.conventionalcommits.org/) (`feat(modern): ...`, `fix(gateway): ...`).
3. **Definition of done** = the CI gates pass locally: `dotnet build` (warnings are errors), `dotnet test`, `dotnet format --verify-no-changes`. The Stop hook blocks finishing while they are red.

## The migration
- `src/Legacy.Api` is the system in production. **It is frozen**: no new behavior. A hook blocks edits; urgent fixes need the developer.
- `src/Modern.Api` re-implements routes with **the same public contract** (URLs and JSON). `tests/.../ParityTests` compares both services; a new difference must fail the build or be listed there with a reason.
- `src/Gateway` (YARP) owns the migration plan: `Migration:Routes` in `appsettings.json`, changeable at runtime. It routes; it holds no business rules and no data access (architecture tests).

## Migration rules
- A route moves **Legacy → Shadow → Canary → Modern**, and rolls back with one call. Skipping Shadow needs a reason.
- **Writes are never shadowed** (they would run twice) and never sent to a service that does not implement them (`ModernReady: false`). `MigrationState` enforces both at startup and at runtime.
- **The modern service reads the legacy database read-only** (`Mode=ReadOnly`). Moving a write route needs its own data store and a sync plan first. A hook blocks SQL writes in `src/Modern.Api`.
- Shadow comparisons run in the background with a bounded queue: the client's latency and answer never depend on the modern service.

## C# conventions
- Classes `sealed` by default; file-scoped namespaces; `TimeProvider` instead of `DateTime.Now`.
- Money is `decimal` (the legacy `double` totals are the bug this migration found). Logging with `[LoggerMessage]`.

## Never
- Edit the SQLite files by hand. Blocked by hook.
- Read or write secrets (`.env`, keys). Blocked by permissions and hook.
- Disable an analyzer or a test to make the build pass.
- Add a dependency without a reason stated in the commit message.
