# Chatty2

_A console app for messaging another computer on the same local network (alpha)._

Chatty2 is a peer-to-peer LAN chat tool. Anything you type without a leading `/` is sent as a
message to whichever peer you're currently connected to. Anything starting with `/` is a command.

## Why this exists

This project is an experiment as much as an app. I wanted to see how different Claude models
perform in distinct roles — one writing, one reviewing — on a lower-level problem than the usual
web or CLI fare. Network programming was a good fit: raw sockets punish sloppiness, and the
connection lifecycle has enough edge cases (listen/dial races, half-open sockets, ungraceful
disconnects) that review has something real to bite on.

The chat app is the vehicle. The workflow is what I was actually testing.

## Rules

- Two computers chat directly over TCP — there is no server, account, or internet connection
  involved. Both sides must be on the same local network (or otherwise able to reach each other's
  IP address and port) and both must be running Chatty2.
- Each running instance passively **listens** for an incoming connection on a port (`53000` by
  default) and can also actively **dial out** to a peer with `/connect`. Whichever happens first —
  someone connecting to you, or you connecting to them — becomes the active chat session.
- Only one peer connection is active at a time in this alpha. After a disconnect (peer-initiated,
  network drop, or your own `/disconnect`), the app automatically goes back to listening, so a
  single run can handle several chat sessions one after another.

## Usage

Build and run the console app:

```
dotnet run --project src/Chatty2.App
```

By default the app listens for incoming connections on port `53000`. To use a different listening
port (e.g. if you're running two instances on the same machine, or `53000` is already in use),
pass `--port` (or its shorthand `-p`):

```
dotnet run --project src/Chatty2.App -- --port 53001
```

An invalid or missing port value (e.g. `--port abc`) prints a usage message and exits with code 1
before any listening starts.

The app shows a `C2>` prompt before reading each line of input.

### Commands

| Command | Description |
| --- | --- |
| `/connect <ip-address> <port>` | Connect to a peer at the given IP address and port. Both arguments are required. |
| `/disconnect` | End the current chat session without closing the app. The app resumes listening automatically. |
| `/help` | Show the available commands and the message-sending rule. |
| `/exit` | Close the application. |

Any line that doesn't start with `/` is sent as a chat message to the connected peer. If you
haven't connected to anyone yet, you'll get a friendly warning instead of silently doing nothing.

### Example: chatting between two computers

On computer A (using the default port):

```
dotnet run --project src/Chatty2.App
```

On computer B, connect to A's IP address on port `53000`:

```
dotnet run --project src/Chatty2.App
/connect 192.168.1.23 53000
```

Once connected, either side can type messages and press Enter to send them. `/disconnect` ends
the session on both sides (each goes back to listening/can reconnect); `/exit` closes the app.

> **Firewall note:** the first time Chatty2 listens for an incoming connection, Windows may prompt
> you to allow it through the firewall. Allow it on private/home networks for LAN chat to work.

## Technical design

This solution follows a console-IO-free domain library plus a thin console orchestration layer,
constructor-injected interfaces with no DI container, and one type per file.

### Project layout

```
src/
  Chatty2.Core/   networking + chat-session domain logic, no Console dependency
  Chatty2.App/    command parsing, the console loop, and the composition root (Program.cs)
test/
  Chatty2.Core.UnitTests/         ChatSession behavior, with the socket layer substituted
  Chatty2.Core.IntegrationTests/  real loopback TCP tests for the socket layer itself
  Chatty2.App.UnitTests/          commands and the console loop, with IChatSession substituted
```

### Transport: TCP with a line-delimited text protocol

Each side of a connection wraps a `TcpClient`'s `NetworkStream` in a `StreamReader`/`StreamWriter`
pair and exchanges messages one line at a time (`IPeerConnection.SendAsync`/`ReceiveAsync`), using
a UTF-8 encoding configured not to emit a BOM preamble (`Encoding.UTF8` would otherwise write a
3-byte marker ahead of the very first message — harmless here only because `StreamReader`'s
default BOM detection strips it back out on the receiving end, but there's no reason to put it on
the wire over a raw socket in the first place). A `null` return from `ReceiveAsync` means the
stream ended cleanly; `TcpPeerConnection.Dispose()` explicitly shuts down only the send direction
(`SocketShutdown.Send`) before closing, so the remote side observes a clean end-of-stream instead
of an abortive "connection reset" — shutting down both directions was tried first and produced
the abortive RST behavior instead, which is why only the send side is shut down.

### Connection lifecycle and the listen/connect race

`ChatSession` (in `Chatty2.Core`) is the only place that decides which connection — if any — is
"the" active one:

- `ListenAsync` loops on `IPeerListener.AcceptAsync`, claiming the first accepted connection as
  active and then stopping. `ConnectAsync` cancels any in-flight listen **as soon as it's called**
  (not when the dial completes), so an explicit `/connect` preempts passive listening immediately
  rather than racing it to completion.
- If a connection attempt and an incoming accept resolve at almost the same instant (the rare
  case where both peers dial each other simultaneously), the connection that arrives second is
  rejected and disposed, and whichever loop lost keeps going (the listener keeps accepting; a
  failed `/connect` re-arms listening) — first connection wins, nothing is left in a stuck state.
  `ConnectAsync` also checks up front whether it's already connected and fails fast in that case,
  *before* tearing down listening or dialing out — without that check, a `/connect` while already
  connected would still cancel listening and open a real `TcpClient` to the target only to reject
  and dispose it a moment later, so the target peer would see a stray connect immediately followed
  by a disconnect for no reason.
- A dropped connection — whether the peer closed cleanly, the network died, or the local user ran
  `/disconnect` — is funneled through one code path: the receive loop treats both a clean `null`
  and any exception from `ReceiveAsync` as "disconnected," clears the active connection, and
  raises a `Disconnected` event. `ConsoleAppRunner` reacts to that event (and to a failed
  `/connect`) by calling `ListenAsync` again, which is what makes the "auto-resume listening"
  behavior work without `ChatSession` needing any retry/restart logic of its own.
- `ListenAsync` is always invoked fire-and-forget by its callers, so any exception that escaped
  it (e.g. a `SocketException` from `TcpListener.Start()` because the port is already in use)
  would otherwise go unobserved — listening would die silently with no feedback. `ListenAsync`
  catches every non-cancellation failure and raises a `ListenFailed` event instead of letting it
  fault the task; `ConsoleAppRunner` surfaces that as a visible error message.
- Each call to `ListenAsync` first awaits the *previous* listen attempt's own completion before
  binding the port again. This matters because re-arming can happen immediately after
  `ConnectAsync` cancels an in-flight listen (e.g. `ConnectCommand` re-arming right after a failed
  dial) — the prior `TcpListener`'s teardown runs asynchronously in its own `finally` block, and
  binding before that teardown finishes can throw "address already in use" (which, pre-fix, was
  exactly the kind of failure point 1 left unobserved). Chaining each attempt behind the last one
  closes that race without requiring any change at the call sites.

### Command pattern

`/connect`, `/disconnect`, `/help`, and `/exit` are each an `ICommand` implementation
(`ConnectCommand`, `DisconnectCommand`, `HelpCommand`, `ExitCommand`). `ConsoleAppRunner` holds a
case-insensitive name-to-command lookup built from whatever commands it's given in its
constructor — adding a new command later means writing one more `ICommand` class and registering
it in `Program.cs`, with no changes to the runner itself. Commands return a `CommandResult`
(an exit flag plus an optional message) rather than writing to the console directly; this keeps
every console write going through one place (see below).

### Observer-style events decouple networking from the console

`IChatSession` exposes `MessageReceived`, `PeerConnected`, and `Disconnected` events instead of
taking a `TextWriter` itself. `Chatty2.Core` has no knowledge of `Console` or any presentation
concern — `ConsoleAppRunner` is the only subscriber, and it's what turns those events into
formatted output. This is also why those event subscriptions happen in `ConsoleAppRunner`'s
constructor rather than in `RunAsync`: it means a test can construct the runner, raise an event
synchronously (via NSubstitute), and only then call `RunAsync` — fully deterministic, with no
sleeps or background threads required to exercise the "received a message while idle" path.

### One output lock for all console writes, color-coded by severity

Received messages can arrive on a background `Task` (the receive loop) at the same time the
foreground loop is about to print its own message (a command's result, a "not connected"
warning, etc.). `ConsoleAppRunner` funnels every write — the loop's own and all three event
handlers' — through one of two private helpers (`WriteInfo`/`WriteError`), both guarded by a
single lock, so a background notification can never interleave mid-line with whatever the
foreground loop is writing.

There are no bracketed prefixes like `[!]` in the output; severity is conveyed by color instead.
Status messages (connected, disconnected, goodbye, help text, and incoming peer messages) print in
yellow. Anything reporting a problem — a caught exception (a failed dial, the top-level unhandled-
exception handler) as well as plain validation failures (an unknown command, an invalid IP/port, a
"not connected" warning) — prints in red. `CommandResult` carries an `IsError` flag so commands can
signal which bucket their message belongs to without needing to know about `Console` at all. After
each line, the foreground color is reset to white. The color is only changed when
`Console.IsOutputRedirected` (or `Console.IsErrorRedirected` for the unhandled-exception path) is
`false`, so piping output to a file or running under a test runner never gets stray ANSI/color
state.

### Known alpha limitations

- Single active peer connection only; no group chat / multiple simultaneous peers.
- No guard against connecting to your own machine's IP/loopback (undefined but harmless).
- The listening port is configurable at startup (`--port`/`-p`); there's no in-app command to
  change it once the app is running.

## Running the tests

```
dotnet test
```

Tests use **xunit.v3** and **NSubstitute**. `Chatty2.Core.UnitTests` and `Chatty2.App.UnitTests`
substitute the networking layer (`IPeerListener`/`IPeerConnector`/`IPeerConnection`) or the chat
session (`IChatSession`) respectively, so they run instantly with no real sockets.
`Chatty2.Core.IntegrationTests` is the one deliberate exception: it exercises real loopback TCP
sockets end-to-end, because a substituted `IPeerConnection` can't catch real-world socket behavior
like the abortive-close issue described above.

## Acknowledgments

Built with Claude on a two-model workflow: Sonnet writes the implementation, Opus peer reviews
every pull request before it merges.
