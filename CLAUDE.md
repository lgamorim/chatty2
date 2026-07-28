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
@.claude/rules/overlays/workflow-agent-review.md
@.claude/rules/archetype/application.md

These are copied file-by-file from the shared [claude-rules](https://github.com/lgamorim/claude-rules)
repository, composed as the `application` archetype under `team` workflow posture, plus
`overlays/workflow-agent-review.md` (this repo runs a standing two-model implement/review flow
over every PR — see `overlays/workflow-agent-review.md` for the contract between those two roles).
That combination matches no single profile in `profiles/`, so the modules are copied directly
rather than through `tools/sync.ps1 -Profile` (which only resolves an exact profile match; it
no longer accepts an ad-hoc `-Workflow` override). Re-audit for drift from the claude-rules
checkout with a per-file hash comparison:

```powershell
$dstRoot = (Resolve-Path .claude\rules).Path
$srcRoot = '<path-to>\claude-rules\.claude\rules'
Get-ChildItem -Recurse -File $dstRoot | ForEach-Object {
    $rel = $_.FullName.Substring($dstRoot.Length).TrimStart('\')
    $src = Join-Path $srcRoot $rel
    if (-not (Test-Path $src)) { "ORPHAN $rel"; return }
    # Compare content, not bytes: the claude-rules worktree and this repo's committed blobs can
    # disagree on line endings (CRLF vs LF) for text that's otherwise identical, which would
    # otherwise make Get-FileHash report false DRIFT.
    $a = (Get-Content $_.FullName -Raw) -replace "`r`n", "`n"
    $b = (Get-Content $src -Raw) -replace "`r`n", "`n"
    if ($a -ne $b) { "DRIFT  $rel" } else { "OK     $rel" }
}
```

## Project-specific notes

- **Archetype is `application`, not `library`.** `Chatty2.Core` is consumed only by `Chatty2.App`
  in this repo and is never packed or published, so the deliverable is the running app.
  `IsPackable` is false for every project and XML docs are required only where intent isn't
  obvious from the code, per `archetype/application.md`.
- **Workflow posture is `team`.** `master` is protected: work lands only via a reviewed pull
  request with a squash merge. Per `overlays/workflow-team.md`, never open a PR automatically —
  confirm with the maintainer first.
- **Reviews follow `overlays/workflow-agent-review.md`.** A separate review agent reads each PR
  fresh, with no implementer context, and leaves inline comments that cite the specific rule
  module a finding violates rather than raising bare style preferences. It never pushes, merges,
  or resolves its own comments. The implementer addresses feedback with follow-up commits on the
  same branch; disagreements go to the maintainer to adjudicate, not back-and-forth between agents.
  The overlay's own text says the implementer "opens the PR" — that's about role separation from
  the reviewer, not a license to skip confirmation: the implementer still confirms with the
  maintainer before opening any PR, per `overlays/workflow-team.md`, which takes precedence here.
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
