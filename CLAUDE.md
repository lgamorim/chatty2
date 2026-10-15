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
@.claude/rules/overlays/workflow-agent-review-team.md
@.claude/rules/archetype/application.md

These are copied from the shared [claude-rules](https://github.com/lgamorim/claude-rules)
repository via its `tools/sync.ps1`, composed as the `application` archetype under `team`
workflow posture, plus `overlays/workflow-agent-review-team.md` (this repo runs a standing two-model
implement/review flow over every PR — see that overlay for the contract between the two roles):

```powershell
./tools/sync.ps1 -Target <path-to>\chatty2 -Profile application-solo -Workflow team -Add workflow-agent-review-team
```

That combination matches no single profile in `profiles/`, so `sync.ps1` copies the modules
without a profile manifest and prints the `@import` lines above to paste here. Re-audit for
drift by re-running the **same flags** with `-Check` — it cannot infer how the set was
composed, so omitting them compares against the wrong set:

```powershell
./tools/sync.ps1 -Target <path-to>\chatty2 -Profile application-solo -Workflow team -Add workflow-agent-review-team -Check
```

`-Check` compares normalized text rather than raw bytes, so a CRLF/LF difference between the
claude-rules worktree and this repo's checkout is not reported as drift.

## Project-specific notes

- **Archetype is `application`, not `library`.** `Chatty2.Core` is consumed only by `Chatty2.App`
  in this repo and is never packed or published, so the deliverable is the running app.
  `IsPackable` is false for every project and XML docs are required only where intent isn't
  obvious from the code, per `archetype/application.md`.
- **Workflow posture is `team`.** `master` is protected: work lands only via a reviewed pull
  request with a squash merge. Per `overlays/workflow-team.md`, never open a PR automatically —
  confirm with the maintainer first.
- **Reviews follow `overlays/workflow-agent-review-team.md`.** A separate review agent reads each PR
  fresh, with no implementer context, and reports findings that cite the specific rule
  module each one violates rather than raising bare style preferences; they reach the PR as
  inline comments plus a summary comment naming the commit reviewed (see the wiring below). It
  never pushes, merges,
  or resolves its own comments. The implementer addresses feedback with follow-up commits on the
  same branch; disagreements go to the maintainer to adjudicate, not back-and-forth between agents.
  The overlay's own text says the implementer "opens the PR" — that's about role separation from
  the reviewer, not a license to skip confirmation: the implementer still confirms with the
  maintainer before opening any PR, per `overlays/workflow-team.md`, which takes precedence here.
  The reviewer role is wired as the `code-reviewer` subagent (`.claude/agents/code-reviewer.md`,
  from the claude-rules `.claude/rules/templates/reviewer-subagent.md`): it has no Edit/Write tools
  and posts nothing itself, so it returns its findings as a report. The implementer session invokes
  it on the maintainer's behalf. For a full review the implementer passes it only the PR
  description (intent, tests added, deliberate deviations) and relays the report verbatim as
  inline PR comments plus a summary comment naming the reviewed commit, leaving them unresolved for
  the maintainer to adjudicate.
- **Each PR gets one full review; later rounds are scoped follow-ups.** After the maintainer
  adjudicates and the accepted findings are fixed, a follow-up review reads only
  `git diff <reviewed-sha>..feature/x` and checks that each accepted finding is resolved and the
  changed lines break no imported rule. The implementer invokes it the same way and passes it
  exactly three inputs: the reviewed SHA and the accepted-findings list, both copied verbatim from
  the maintainer's adjudication rather than restated in the implementer's own words, plus the
  implementer's replies on those comment threads. Its report is relayed the same way as a full
  review's. The maintainer may waive the
  follow-up and approve directly, or ask for a second full review. Before that second review, the
  PR description is rewritten to describe the whole PR as it stands.
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
- **Versioning.** `<Version>` in `Directory.Build.props` names the last *released* version and
  changes only in the PR that cuts a release, together with `CHANGELOG.md` and a matching `vX.Y.Z`
  tag. Cut the tag from `master` *after* the release PR is squash-merged, never from the feature
  branch: a branch tip is replaced by a different squash commit and the tag is left pointing at a
  commit `master` doesn't contain (as `v0.1.0` does). A PR with a user-visible change (a command, flag, output, or wire-protocol change) adds a
  line under `## [Unreleased]` in `CHANGELOG.md`; refactors, tests and rule resyncs do not.
