# Archetype — Application (single deployable)

For a runnable end product: console app, service, API host, or game host.
Compose with the five core rules.

Structure (deltas from `core/architecture.md`)
- Tests under `test/<ProjectName>.UnitTests/`; add a `.IntegrationTests` project
  only when a real dependency is exercised.
- No `dotnet pack` — the deliverable is the running app, not a package.

Conventions
- The app is the boundary, not a consumed API: XML docs are required only where
  intent isn't obvious from the code (core's public-API-docs rule relaxes here).
- Configuration comes from `appsettings*.json` + environment/user-secrets;
  never hard-code environment-specific values or secrets.
