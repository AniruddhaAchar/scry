# ADR 0002 — gRPC over Unix domain sockets and named pipes

- **Status:** Accepted
- **Date:** 2026-06-10

## Context

`scry` (client) and `scryd` (host) are separate processes that must exchange structured commands
and results. The host is a Kestrel-hosted gRPC server; the question is only what it binds to.

Two viable options:

| Transport | Pros | Cons |
|---|---|---|
| **UDS (Linux/macOS) + named pipe (Windows)** | No open port; access scoped by filesystem permissions; matches the .NET diagnostics convention. | Two platform code paths; client needs a `SocketsHttpHandler.ConnectCallback` to dial the socket/pipe. |
| **TCP loopback (127.0.0.1:port)** | One uniform code path; trivial tooling. | Opens a local port any same-user process can connect to → must bind loopback-only and require a per-session token. |

Dumps routinely contain secrets (connection strings, tokens, PII), so limiting who can connect to a
host holding one matters. The gRPC server itself is cheap regardless of bind target — the expensive
work is `DataTarget.LoadDump` + DAC load, which happens once at host startup.

## Decision

Bind over **UDS on Unix and named pipes on Windows**, serving HTTP/2 cleartext (h2c). There is no
TLS: access is scoped by filesystem permissions instead. TCP loopback (with a per-session token) is
held in reserve as a fallback flag if a uniform transport is ever needed.

The endpoint name is derived deterministically from the dump path (`scry-<16 hex of SHA-256>`, see
`ScryEndpoint`), so any `scry` invocation for a given dump finds the one `scryd` serving it.

- **Host:** `Kestrel.ListenNamedPipe` / `ListenUnixSocket` with `HttpProtocols.Http2`. A stale Unix
  socket file is deleted before bind.
- **Client:** a `GrpcChannel` whose `SocketsHttpHandler.ConnectCallback` dials a
  `NamedPipeClientStream` (bounded connect timeout) or a `Socket` on a `UnixDomainSocketEndPoint`.

## Consequences

- No listening TCP port; least-privilege access via the filesystem.
- Two transport code paths to maintain, plus the client-side connect callback. On this Windows
  development box only the named-pipe path is exercised end-to-end; the UDS path is covered on
  Linux/macOS.
- Connection failures (host not running) are mapped to a clean `UNAVAILABLE` error with a hint,
  rather than a leaked transport exception.
- A short named-pipe connect timeout makes "no host" fail fast instead of blocking until the RPC
  deadline; M1's spawn-on-miss creates the pipe well within that window.
