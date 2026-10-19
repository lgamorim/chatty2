# Changelog

All notable changes to Chatty2 are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html); while the app is
in alpha (`0.x`), minor releases may include breaking changes to behavior or
the wire protocol.

## [Unreleased]

### Added

- `--log <path>` appends a timestamped record of the conversation (messages in both directions
  plus connect and disconnect notices) to a file. It is off by default; the file is plain text.
- `/status` shows your display name, the port you were started listening on, and who you are
  connected to (name and address), or that you are not connected.
- `/redial` reconnects to the peer last reached with `/connect`, so a dropped session doesn't
  mean retyping the address. Peers that dialed in are not remembered.

### Changed

- The disconnect notice now names the peer and says what happened: `Bob disconnected.` when the
  peer ended the session, `Connection to Bob was lost.` when the connection dropped, and
  `Disconnected from Bob.` after your own `/disconnect` (previously all three printed
  `Peer disconnected.`). The wire protocol is unchanged, so older peers are unaffected.
- Chat messages are limited to 4096 characters. Typing a longer line is refused locally with a
  "too long" message. A longer line received from a peer (for example from an older version,
  which has no limit) is discarded with a notice and the session stays open.

## [0.1.0] - 2026-08-11

Initial alpha release.

### Added

- Peer-to-peer LAN chat over raw TCP: each instance listens on a port
  (`53000` by default, set with `--port`/`-p`) and can dial out with
  `/connect <ip> <port>`; whichever happens first becomes the active session.
- `/disconnect`, `/help` and `/exit` commands. After any disconnect the app
  returns to listening.
- A `C2> ` prompt before each line of input.
- Display names with `--name`/`-n` (defaulting to the OS user name), exchanged
  with the peer in a `NAME:` handshake.
- A local-time `[HH:mm:ss]` timestamp on incoming messages.

[Unreleased]: https://github.com/lgamorim/chatty2/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/lgamorim/chatty2/releases/tag/v0.1.0
