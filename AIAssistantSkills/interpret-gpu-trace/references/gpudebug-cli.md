# `gpudebug` CLI reference

Verified 2026-06-23 on macOS 27.0 (build 26A5353q), device `Mac15,6` (id 0), binary `/usr/bin/gpudebug`.
Reports itself as **GPU Debugger (v1.0)**.

`gpudebug` replays/inspects a `.gputrace` (or attaches to a live session) and exposes an interactive REPL.

## Options

| Flag | Long | Meaning |
|------|------|---------|
| `-t` | `--gputrace` | path to a `.gputrace` file |
| `-s` | `--session` | connect to an existing session by ID |
| `-l` | `--list-sessions` | list active debug sessions and exit |
| `-d` | `--device` | target device for replay |
|      | `--list-devices` | list available replay devices and exit |
|      | `--terminate` | terminate a session by ID, or `all` |
|      | `--oneshot` | terminate the session after commands complete |
| `-o` | `--output` | output directory for fetched resources |
| `-q` | `--quiet` | suppress the startup banner |
|      | `--json` | **output in JSON format** |
|      | `--timeout` | idle timeout in seconds (`0` = disable) |
|      | `--version` | show version |
| `-h` | `--help` | show help |

> **Important update:** `gpudebug` v1.0 supports `--json`. Earlier notes assumed text-only output —
> prefer `--json` for structured, agent-friendly parsing instead of scraping REPL text.

## Driving it non-interactively (for the skill)

```
gpudebug -t <path>/frame.gputrace --json --oneshot -q [-o <fetch-dir>] < commands.txt
```

- `--oneshot` so the session ends after the piped commands finish (no hang).
- `--json` for machine-readable results.
- `-o` to collect fetched resources (textures/buffers) for Unity-context correlation.

### Needs runtime confirmation (requires a real `.gputrace`)
- Exact REPL command set. Expected from prior notes: `list / go / info / fetch / find / next / prev / status`.
  Confirm names, arguments, and which honor `--json` once a trace is loaded.
- How encoder / pass / draw labels appear, so they can be mapped to Unity SRP (URP) `ScriptableRenderPass`,
  shader/material names, and Profiler / `CommandBuffer` markers.
