# Chatty2

[![CI](https://github.com/lgamorim/chatty2/actions/workflows/ci.yml/badge.svg)](https://github.com/lgamorim/chatty2/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

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
  single run can handle several chat sessions one after another. The notice names the peer and
  says which of the three happened, e.g. `Bob disconnected.`, `Connection to Bob was lost.` or
  `Disconnected from Bob.`

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

The app shows a `C2>` prompt before reading each line of input. The prompt is omitted when input
is redirected (e.g. piped in from a script), since there's no one watching it.

By default your display name is your OS login name. To use a different one, pass `--name` (or its
shorthand `-n`):

```
dotnet run --project src/Chatty2.App -- --name Alice
```

Right after connecting, each side privately tells the other its display name, so incoming messages
are labeled with the peer's actual name (e.g. `[Alice] hello`) instead of the generic `[peer]` used
until it's known. This means the first line on the wire after connecting is reserved for that
exchange, so a display name is limited to 1-64 characters (counted the simple way - a name using
emoji or other characters outside the common range may hit the limit sooner) and cannot contain a
line break.

Each incoming message is also prefixed with the local time it arrived,
e.g. `[14:30:05] [Alice] hello`.

A chat message is limited to 4096 characters. Typing a longer line is refused locally with a
"too long" message and nothing is sent. If a peer sends a longer line anyway (an older version
has no limit), it is discarded and you get a notice naming the peer; the session stays open and
the lines after it arrive normally.

To keep a record of your conversations, pass `--log` with a file path (there is no short form):

```
dotnet run --project src/Chatty2.App -- --log chat.log
```

Logging is off unless you ask for it. The file is appended to (and created if it does not exist),
so it keeps growing across runs; the parent directory must already exist. Each line carries the
local time with its UTC offset, because the file can span days and time zones:

```
2026-10-19T01:06:59+01:00 * Connected to 127.0.0.1:56513.
2026-10-19T01:06:59+01:00 [Raw] hi from raw
2026-10-19T01:07:08+01:00 [Alice] hello from Alice
2026-10-19T01:07:08+01:00 * Raw disconnected.
```

Messages in both directions are logged (yours under your own display name), along with the same
connect, disconnect and dropped-message notices the console prints, marked with `*`. Command
output, errors and the prompt are not. A path that cannot be opened prints the reason, then the
usage message, and exits with code 1 before anything starts listening. If writing fails later
(a full disk, say), you get one error, logging stops for the rest of the session and the chat
carries on.

The log is plain text on your disk with no rotation or encryption: anything said in the chat
ends up in it, so treat the file accordingly. It is held open for appending while the app runs.
On Windows that stops a second instance from opening the same file; other platforms may not
enforce it, in which case both instances would append to it and their lines would interleave.
Either way, give each instance its own file.

### Commands

| Command | Description |
| --- | --- |
| `/connect <ip-address> <port>` | Connect to a peer at the given IP address and port. Both arguments are required. |
| `/redial` | Reconnect to the peer you last reached with `/connect`, so you don't have to retype the address after a drop. Takes no arguments. |
| `/disconnect` | End the current chat session without closing the app. The app resumes listening automatically. |
| `/status` | Show your display name, the port you were started listening on, and whether you are connected (to whom, and from which address). Takes no arguments. |
| `/help` | Show the available commands and the message-sending rule. |
| `/exit` | Close the application. |

Any line that doesn't start with `/` is sent as a chat message to the connected peer. If you
haven't connected to anyone yet, you'll get a friendly warning instead of silently doing nothing.

`/redial` only remembers peers *you* dialed: the last `/connect` that completed its handshake,
kept in memory for the life of the process. A peer that connected to you isn't remembered, since
the port it connects from is chosen by its OS and can't be dialed back. A failed `/connect` or
`/redial` leaves the remembered peer unchanged, and with nothing remembered `/redial` tells you
to use `/connect`.

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

Incoming lines are not read with `StreamReader.ReadLineAsync` directly, because that has no length
limit and a peer could make the app buffer an unbounded amount of text. `TcpPeerConnection` reads
through `BoundedLineReader`, which keeps the same `\r`, `\n` and `\r\n` line breaks but holds at
most `ChatSession.MaxMessageLength` (4096) characters of any one line. A longer line is discarded
up to its line break and reported by throwing `MessageTooLongException`; the stream is then
positioned at the next line, so `ChatSession` raises `MessageDropped` and keeps receiving instead of
ending the session. The limit is counted in UTF-16 characters of the decoded line, the same unit
used for the sender-side check in `ChatSession.SendAsync`. A peer that never sends a line break
still cannot grow memory past the limit, but the discard simply waits for one; there is no timeout
(see the limitations below).

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
- The `Disconnected` event says why the session ended and who it was with
  (`DisconnectedEventArgs`: a `DisconnectReason` and the peer's name, `null` if it never
  identified itself), so the console can print `Bob disconnected.` (the peer ended the stream
  cleanly), `Connection to Bob was lost.` (reading failed, e.g. a reset) or
  `Disconnected from Bob.` (this side ran `/disconnect`). A `/disconnect` or `Dispose` marks the
  connection under the same `_gate` lock before closing it, and the receive loop reports that as
  the local reason however the socket reacts to being closed. On exit the runner unsubscribes
  before disposing the session, so nothing is printed then. This uses only what TCP already tells us, so
  the wire protocol is unchanged and older peers are unaffected. The limit is that it cannot tell
  a peer that quit on purpose from one that crashed but still sent an orderly end of stream; both
  read as "disconnected". A peer that closes while the other side's handshake line is still
  unread can produce a reset. Whether that reads as "lost" or as an orderly close depends on the
  platform's TCP stack, which can report a clean end of stream when a reset follows a FIN.
- `ChatSession.Peer` (a `PeerInfo`: the connection's remote endpoint plus the peer's name once its
  handshake has been read, `null` when not connected) is kept beside `_activeConnection` and
  changes under the same `_gate` lock: set when a connection is claimed, given its name just
  before `PeerIdentified` is raised, and cleared before `Disconnected` is raised, so `/status` and
  event handlers never see a stale peer. The runner's own peer name, which labels incoming
  messages, is separate.
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

`/connect`, `/redial`, `/disconnect`, `/status`, `/help`, and `/exit` are each an `ICommand`
implementation (`ConnectCommand`, `RedialCommand`, `DisconnectCommand`, `StatusCommand`,
`HelpCommand`, `ExitCommand`;
`/connect` and `/redial` share the dial-and-recover logic in `PeerDialer`). `ConsoleAppRunner` holds a
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

### The chat log is written from the runner, under the output lock

`ChatLog` (in `Chatty2.App`) turns a message or event into one timestamped, flushed line on a
`TextWriter` and knows nothing about the console or the network. `ConsoleAppRunner` is what feeds
it, because it is the one place that sees both directions: `ChatSession` raises an event for each
incoming message but has none for the messages the user sends. Every log write goes through one
helper that takes the same lock as the console writes, so lines reach the file one at a time and
never interleave, and that helper is also where a failing writer is handled (report once, stop
logging, keep chatting). It does not promise that the file's order matches the screen's: an
event's screen write and its log write take the lock separately, so two events on different
threads can swap places in the file, and the messages you send are logged without ever being
shown. The text of each notice is built once and used for both the screen and the log, so the
two cannot drift apart. `Program.cs` only opens the file;
it is the one untested seam, covered by running the app.

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
- No read timeout or heartbeat: a connected peer that goes silent (or never finishes a line) holds
  the single connection slot until it disconnects or you run `/disconnect`.
- `/status` shows the port the app was started with, not whether listening is currently active
  (for example, it still shows the port after a "Stopped listening" error or while a `/connect`
  is dialing). The connection line, by contrast, always reflects the live session.
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
