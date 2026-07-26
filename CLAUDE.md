# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Chatty2 is a peer-to-peer LAN chat console app over raw TCP (alpha). Two instances talk directly —
no server, no account, no internet. Each instance passively **listens** on a port (`53000` by
default) and can also actively **dial out** with `/connect`; whichever happens first becomes the
active session. Only one peer connection is active at a time, and after any disconnect the app
returns to listening. Input starting with `/` is a command; anything else is a message to the peer.
See [README.md](README.md) for full user-facing behavior.

## Structure

```
src/Chatty2.Core   TCP transport + session lifecycle (IPeerListener/IPeerConnector/IPeerConnection,
                   ChatSession). No console dependency.
src/Chatty2.App    Console host: ConsoleAppRunner, ICommand implementations, Program.cs wiring.
test/Chatty2.Core.UnitTests          deterministic, no sockets
test/Chatty2.App.UnitTests           deterministic, no sockets
test/Chatty2.Core.IntegrationTests   real loopback TCP — the only place sockets are opened
```

Dependencies point one way: `App` → `Core`. Never add a console/UI dependency to `Core`.

## Commands

```powershell
dotnet build Chatty2.slnx                 # zero warnings required (TreatWarningsAsErrors)
dotnet test Chatty2.slnx                  # all three test projects
dotnet format Chatty2.slnx                # must pass; also the naming gate (see below)
dotnet run --project src/Chatty2.App                    # listen on the default port
dotnet run --project src/Chatty2.App -- --port 53001    # listen on a specific port
```

Run a single test project or test:

```powershell
dotnet test test/Chatty2.Core.UnitTests
dotnet test Chatty2.slnx --filter "FullyQualifiedName~ChatSessionTests"
```

Two instances on one machine: run each with a different `--port`, then `/connect <ip> <port>`.

## Conventions
@.claude/rules/core/coding-standards.md
@.claude/rules/core/design-principles.md
@.claude/rules/core/architecture.md
@.claude/rules/core/testing-philosophy.md
@.claude/rules/core/workflow-core.md
@.claude/rules/overlays/workflow-team.md
@.claude/rules/archetype/application.md

These are copied from the shared [claude-rules](https://github.com/lgamorim/claude-rules)
repository via its `tools/sync.ps1`, composed as `application-solo -Workflow team`. Because that
combination matches no profile, the modules are imported directly rather than through a profile
manifest. Re-audit for drift from the claude-rules checkout with:

```powershell
./tools/sync.ps1 -Target <path-to>\chatty2 -Profile application-solo -Workflow team -Check
```

Pass those exact flags — `-Check` cannot infer how the set was composed.

## Project-specific notes

- **Archetype is `application`, not `library`.** `Chatty2.Core` is consumed only by `Chatty2.App`
  in this repo and is never packed or published, so the deliverable is the running app.
  `IsPackable` is false for every project and XML docs are required only where intent isn't
  obvious from the code, per `archetype/application.md`.
- **Workflow posture is `team`.** `master` is protected: work lands only via a reviewed pull
  request with a squash merge. Per `overlays/workflow-team.md`, never open a PR automatically —
  confirm with the maintainer first.
- `Directory.Build.props` centralizes `TargetFramework`, `Nullable`, `ImplicitUsings`,
  `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, and `IsPackable` per `core/architecture.md`,
  so the `.csproj` files carry only what is specific to them (`OutputType`, package references).
- `GenerateDocumentationFile` is on because build-time `IDE0005` (unused usings) requires it, but
  `CS1591` (missing XML doc) is in `NoWarn`: the application archetype explicitly relaxes the
  public-API-docs rule, and enabling one shouldn't smuggle in the other.
- **`dotnet format` is the naming gate, not the build.** `EnforceCodeStyleInBuild` does not
  surface `IDE1006` naming violations as build errors in this SDK, so a green build does not prove
  naming compliance. Run `dotnet format Chatty2.slnx --verify-no-changes --severity warn`.
- Sockets belong in `Chatty2.Core.IntegrationTests` only. Unit tests must stay deterministic with
  no real I/O, network, clock, or `Task.Delay`, per `core/testing-philosophy.md`.
- `ChatSession` guards its state with a `Lock` (`_gate`) because listen and dial race by design;
  keep new state transitions inside that lock rather than adding a second synchronization scheme.
