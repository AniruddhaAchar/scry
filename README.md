# scry

A [ClrMD](https://github.com/microsoft/clrmd)-based .NET memory-dump analyzer **for AI agents**. It
returns **structured JSON**, not human-readable SOS text, so an agent can chain analysis steps
(`stat` → list of a type → dump an object → walk roots) without scraping a debugger's console.

> **Status: v0.0.1, milestone M0 (skeleton).** The transport, daemon lifecycle, and CLI are in
> place; ClrMD dump loading and the analysis commands land in M1+. See
> [the roadmap](CLAUDE.md#milestones).

## How it works

Two binaries:

| Binary  | Lifetime    | Role |
|---------|-------------|------|
| `scryd` | long-lived  | Host daemon. Loads one dump with ClrMD, holds the runtime open, serves commands over gRPC. |
| `scry`  | short-lived | The CLI an agent invokes per command. Connects to the host for a dump, issues one gRPC call, prints JSON, exits. |

Loading a dump is expensive (seconds, gigabytes of RAM), so the daemon stays warm between
commands. The client finds the right daemon by deriving a deterministic endpoint id from the dump
path, and talks to it over a **named pipe** (Windows) or **Unix domain socket** (Linux/macOS) — no
network port is opened. See [ADR 0002](docs/adr/0002-grpc-over-uds-and-named-pipes.md).

## Quickstart (M0)

Requires the **.NET 10 SDK**.

```bash
dotnet build scry.slnx

# Start a host for a dump. In M0 the dump isn't read yet — the path only seeds the endpoint.
scryd --dump /path/to/app.dmp --idle-timeout 10

# From another shell, talk to it:
scry health   --dump /path/to/app.dmp
scry shutdown --dump /path/to/app.dmp
```

`scry health` prints, for example:

```json
{
  "endpoint": "scry-8dab0db081f14f3e",
  "state": "READY",
  "runtimeVersion": "",
  "detail": "M0 skeleton: no runtime loaded"
}
```

If no host is running for that dump, you get a structured error and a non-zero exit:

```json
{
  "error": {
    "code": "UNAVAILABLE",
    "message": "no scryd host is reachable for this dump (endpoint scry-...)",
    "hint": "start one with: scryd --dump \"/path/to/app.dmp\""
  }
}
```

## Development

```bash
dotnet test   scry.slnx --filter Category=Unit     # fast unit tests
dotnet format scry.slnx --verify-no-changes        # formatting gate
```

Pre-commit hooks (formatting + unit tests) are managed with
[prek](https://github.com/j178/prek):

```bash
prek install        # one-time
```

Package versions are centralized in `Directory.Packages.props`; `packages.lock.json` files are
committed for reproducible restores.

## License

MIT (see [LICENSE](LICENSE)).
