# Changelog

All notable changes to Chatty2 are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html); while the app is
in alpha (`0.x`), minor releases may include breaking changes to behavior or
the wire protocol.

## [Unreleased]

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
