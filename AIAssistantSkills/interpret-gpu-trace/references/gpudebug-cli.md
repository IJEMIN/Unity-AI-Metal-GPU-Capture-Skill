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

## REPL commands (confirmed 2026-06-25 against a real URP Standalone capture, Apple M3 Pro)
`list`, `go <name|url>`, `info [--all]`, `find <text>`, `next` / `prev`, `fetch`, `status`, `wait`,
`profile`, `help`, `exit`. Per-command help: `<command> ?`. Navigation tree:
`commands/cb<N>/grp<N>/re<N>/draw<N>`, `api_calls`, `resources`, and (after profiling) `performance`.

## GPU timing / profiling — how to get GPU frame time
The `performance` subtree is EMPTY until a profiling session is loaded.
- `profile` — status; `profile list` — embedded sessions in the trace.
- `profile load [N]` — load an embedded session (~15-18s; replayer must be ready).
- `profile run [--gpu-state low|medium|high] [--exec overlapping|serial] [--embed]` — collect from the
  device; **requires an M3/A17+ replay GPU**.
- After loading:
  - `go performance/timeline` + `info --all` → whole-frame **GPU time** (e.g. `GPU time 31.1000 ms`,
    plus `Frame duration`, `Frame begin/end` ns).
  - `go performance/encoders` (sorted by cost) → **per-pass GPU time** (Cost %, Vertex/Fragment/Compute ms)
    — the top render pass by GPU cost.
  - `go performance/shaders` / `performance/commands` → per-shader / per-draw cost.
  - `go performance/timeline/counters` → 30 GPU counter groups; counter time-series fetchable as JSON.

**CPU frame time is NOT in a GPU trace** (no `info` on `commands` / `cb<N>`). Get CPU frame time from
Unity (`FrameTimingManager` / ProfilerRecorder), not from gpudebug.

### Still to confirm
- Which `performance` views emit JSON via `--json` directly vs. needing `fetch` to write JSON.
- Encoder/pass label → URP `ScriptableRenderPass` mapping (see the skill's mapping table).
